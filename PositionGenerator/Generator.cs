using ChessDotNet;
using ChessDotNet.Variants.Atomic;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
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
        BlockingCollection<string> fenQueue;
        ManualResetEvent mre = new ManualResetEvent(false);

        public ulong GamesSeen
        {
            get;
            private set;
        }

        public ulong AtomicGames
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
            fenQueue = new BlockingCollection<string>();
            GamesSeen = 0;
            AtomicGames = 0;
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
            bool nextGameWillBeAtomic = false;
            PgnReader<AtomicChessGame> pgnReader = null;
            AtomicChessGame copy = null;
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
                    nextGameWillBeAtomic = false;
                    if (!string.IsNullOrWhiteSpace(pgn))
                    {
                        pgnReader.ReadPgnFromString(pgn);
                        string fen;
                        ReadOnlyCollection<DetailedMove> moves = pgnReader.Game.Moves;
                        for (int i = 0; i < moves.Count; i++)
                        {
                            copy.ApplyMove(moves[i], true);
                            fen = copy.GetFen();
                            ReadOnlyCollection<Move> validMoves = copy.GetValidMoves(copy.WhoseTurn);
                            for (int j = 0; j < validMoves.Count; j++)
                            {
                                AtomicChessGame copy2 = new AtomicChessGame(fen);
                                copy2.ApplyMove(validMoves[j], true);
                                if (copy2.IsCheckmated(copy2.WhoseTurn) || copy2.KingIsGone(copy2.WhoseTurn))
                                {
                                    fenQueue.Add(fen);
                                    break;
                                }
                            }
                        }
                    }
                }
                if (line.StartsWith("[") && !currentlyInGame)
                {
                    if (line == "[Variant \"Atomic\"]")
                    {
                        nextGameWillBeAtomic = true;
                    }
                    continue;
                }
                if (!currentlyInGame)
                {
                    GamesSeen++;
                    if (!nextGameWillBeAtomic) continue;
                    AtomicGames++;
                    currentlyInGame = true;
                    pgn = string.Empty;
                    pgnReader = new PgnReader<AtomicChessGame>();
                    copy = new AtomicChessGame();
                }
                pgn += line;
            }
            sr.Close();
            fenQueue.CompleteAdding();
            mre.WaitOne();
        }

        void AddFens()
        {
            string fen = null;
            while (!fenQueue.IsCompleted)
            {
                try
                {
                    fen = fenQueue.Take();
                }
                catch (InvalidOperationException e)
                {
                    Console.WriteLine("[note] InvalidOperationException on .Take(). Most likely, CompleteAdding was called during Take. The application will proceed. Exception message: " + e.Message);
                    continue;
                }
                TrainingPosition position = new TrainingPosition();
                position.FEN = position.ID = fen;
                position.Location = new double[2] { rnd.NextDouble(), rnd.NextDouble() };
                position.Type = "mateInOne";

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
