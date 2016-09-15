using ChessDotNet;
using ChessDotNet.Pieces;
using ChessDotNet.Variants.Antichess;
using ChessDotNet.Variants.Atomic;
using ChessDotNet.Variants.Horde;
using ChessDotNet.Variants.KingOfTheHill;
using ChessDotNet.Variants.ThreeCheck;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace PositionGenerator
{
    class Generator
    {
        string connectionString;
        string dbName;
        string positionCollectionName;
        string filepath;
        IMongoCollection<TrainingPosition> positionCollection;
        Random rnd;
        BlockingCollection<Tuple<string, string[], string, string>> fenQueue;
        ManualResetEvent mre = new ManualResetEvent(false);

        Dictionary<string, string> normalizedVariantNames = new Dictionary<string, string>()
        {
            { "Antichess", "Antichess" },
            { "Atomic", "Atomic" },
            { "King of the Hill", "KingOfTheHill" },
            { "Horde", "Horde" },
            { "Three-check", "ThreeCheck" }
        };

        public ulong GamesSeen
        {
            get;
            private set;
        }

        public ulong GamesUsed
        {
            get;
            private set;
        }

        public ulong UniquePositionsAdded
        {
            get;
            private set;
        }

        public Generator(string mongoConnectionString, string databaseName, string mongoPositionCollectionName, string pgnFilePath)
        {
            connectionString = mongoConnectionString;
            dbName = databaseName;
            positionCollectionName = mongoPositionCollectionName;
            filepath = pgnFilePath;
            rnd = new Random();
            fenQueue = new BlockingCollection<Tuple<string, string[], string, string>>();
            GamesSeen = 0;
            GamesUsed = 0;
            UniquePositionsAdded = 0;

            GetCollection();
        }

        void GetCollection()
        {
            MongoClient client = new MongoClient(connectionString);
            positionCollection = client.GetDatabase(dbName).GetCollection<TrainingPosition>(positionCollectionName);
        }

        public void DoWork()
        {
            Thread thr = new Thread(AddFens);
            thr.Start();

            StreamReader sr = new StreamReader(filepath);
            string line;
            bool currentlyInGame = false;
            bool processNextGame = false;
            string variantNextGame = null;
            string initialFenNextGame = null;
            string pgn = null;
            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (line == string.Empty)
                {
                    continue;
                }
                if (line.StartsWith("[") && currentlyInGame)
                {
                    currentlyInGame = false;
                    processNextGame = false;
                    string initialFen = initialFenNextGame;
                    initialFenNextGame = null;
                    if (!string.IsNullOrWhiteSpace(pgn))
                    {
                        switch (variantNextGame)
                        {
                            case "Atomic":
                                GenerateMateInOnePositions(pgn, fen => new AtomicChessGame(fen), "Atomic", initialFen);
                                break;
                            case "Horde":
                                GenerateMateInOnePositions(pgn, fen => new HordeChessGame(fen), "Horde", initialFen);
                                break;
                            case "KingOfTheHill":
                                GenerateMateInOnePositions(pgn, fen => new KingOfTheHillChessGame(fen), "KingOfTheHill", initialFen);
                                break;
                            case "ThreeCheck":
                                GenerateMateInOnePositions(pgn, fen => new ThreeCheckChessGame(fen), "ThreeCheck", initialFen);
                                break;

                            case "Antichess":
                                GenerateAntichessForcedCapturePositions(pgn);
                                break;
                        }
                    }
                }
                if (line.StartsWith("[") && !currentlyInGame)
                {
                    Match variantMatch, fenMatch;
                    if ((variantMatch = Regex.Match(line, "\\[Variant \"(Antichess|Atomic|Horde|King of the Hill|Three-check)\"\\]")).Success)
                    {
                        processNextGame = true;
                        variantNextGame = normalizedVariantNames[variantMatch.Groups[1].Value];
                    }
                    if ((fenMatch = Regex.Match(line, "\\[FEN \"([^\"]+)\"\\]")).Success)
                    {
                        initialFenNextGame = fenMatch.Groups[1].Value;
                    }
                    continue;
                }
                if (!currentlyInGame)
                {
                    GamesSeen++;
                    if (!processNextGame) continue;
                    GamesUsed++;
                    currentlyInGame = true;
                    pgn = string.Empty;
                }
                pgn += line;
            }
            sr.Close();
            fenQueue.CompleteAdding();
            mre.WaitOne();
        }

        void GenerateMateInOnePositions<TGame>(string pgn, Func<string, TGame> createFromFen, string variant, string initialFen) where TGame : ChessGame, new()
        {
            if (variant == "Horde" && initialFen.StartsWith("pppppppp"))
            {
                return;
                // Once upon a time on lichess, the Black player had the Pawns and the White player the Pieces. That has been changed now, resulting in several invalid PGNs.
            }
            PgnReader<TGame> pgnReader = new PgnReader<TGame>();
            TGame copy = new TGame();

            pgnReader.ReadPgnFromString(pgn);
            string fen;
            ReadOnlyCollection<DetailedMove> moves = pgnReader.Game.Moves;
            if (moves.Count >= 3) // there will never be a mate-in-one position if white hasn't done two moves
            {
                for (int i = 0; i < moves.Count; i++)
                {
                    copy.ApplyMove(moves[i], true);
                    if (i < 2) continue; // there will never be a mate-in-one position if white hasn't done two moves
                    if (copy is ThreeCheckChessGame)
                    {
                        ThreeCheckChessGame g = copy as ThreeCheckChessGame;
                        if ((g.WhoseTurn == Player.White ? g.ChecksByWhite : g.ChecksByBlack) != 2) continue;
                    }
                    fen = copy.GetFen();
                    ReadOnlyCollection<Move> validMoves = copy.GetValidMoves(copy.WhoseTurn);
                    DetailedMove lastMove = moves[i];
                    for (int j = 0; j < validMoves.Count; j++)
                    {
                        TGame copy2 = createFromFen(fen);
                        copy2.ApplyMove(validMoves[j], true);
                        if (copy2.IsWinner(ChessUtilities.GetOpponentOf(copy2.WhoseTurn)))
                        {
                            fenQueue.Add(new Tuple<string, string[], string, string>(fen,
                                new string[] { lastMove.OriginalPosition.ToString().ToLowerInvariant(), lastMove.NewPosition.ToString().ToLowerInvariant() }, variant, "mateInOne"));
                            break;
                        }
                    }
                }
            }
        }

        void GenerateAntichessForcedCapturePositions(string pgn)
        {
            PgnReader<AntichessGame> pgnReader = new PgnReader<AntichessGame>();
            AntichessGame copy = new AntichessGame();

            pgnReader.ReadPgnFromString(pgn);
            string fen;
            ReadOnlyCollection<DetailedMove> moves = pgnReader.Game.Moves;
            if (moves.Count >= 2)
            {
                for (int i = 0; i < moves.Count; i++)
                {
                    copy.ApplyMove(moves[i], true);
                    if (i < 1) continue;
                    fen = copy.GetFen();
                    ReadOnlyCollection<Move> validMoves = copy.GetValidMoves(copy.WhoseTurn);
                    DetailedMove lastMove = moves[i];
                    if (validMoves.Count != 1) continue;
                    if (copy.GetPieceAt(validMoves[0].NewPosition) != null ||
                        (copy.GetPieceAt(validMoves[0].OriginalPosition) is Pawn && validMoves[0].OriginalPosition.File != validMoves[0].NewPosition.File))
                    {
                        fenQueue.Add(new Tuple<string, string[], string, string>(fen,
                            new string[] { lastMove.OriginalPosition.ToString().ToLowerInvariant(), lastMove.NewPosition.ToString().ToLowerInvariant() }, "Antichess", "forcedCapture"));
                    }
                }
            }
        }

        void AddFens()
        {
            Tuple<string, string[], string, string> fenAndLastMoveAndVariant = null;
            string fen = null;
            string[] lastMove = null;
            string variant = null;
            string type = null;
            while (!fenQueue.IsCompleted)
            {
                try
                {
                    fenAndLastMoveAndVariant = fenQueue.Take();
                }
                catch (InvalidOperationException e)
                {
                    Console.WriteLine("[note] InvalidOperationException on .Take(). Most likely, CompleteAdding was called during Take. The application will proceed. Exception message: " + e.Message);
                    continue;
                }
                fen = fenAndLastMoveAndVariant.Item1;
                lastMove = fenAndLastMoveAndVariant.Item2;
                variant = fenAndLastMoveAndVariant.Item3;
                type = fenAndLastMoveAndVariant.Item4;
                TrainingPosition position = new TrainingPosition();
                position.FEN = position.ID = fen;
                position.Location = new double[2] { rnd.NextDouble(), rnd.NextDouble() };
                position.Type = type + variant;
                position.LastMove = lastMove;
                position.Variant = variant;

                var found = positionCollection.Find(new BsonDocument("_id", new BsonString(fen)));
                if (found == null || found.Count() == 0)
                {
                    positionCollection.InsertOne(position);
                    UniquePositionsAdded++;
                }
            }
            mre.Set();
        }
    }
}
