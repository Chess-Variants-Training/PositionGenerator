using System;

namespace PositionGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("MongoDB connection string: ");
            string connectionString = Console.ReadLine();
            Console.WriteLine("Database name: ");
            string databaseName = Console.ReadLine();
            Console.WriteLine("Positions collection name: ");
            string collectionName = Console.ReadLine();
            Console.WriteLine("PGN file path: ");
            string path = Console.ReadLine();

            Console.WriteLine("Started at " + DateTime.Now.ToLongDateString() + " " + DateTime.Now.ToLongTimeString());
            DateTime started = DateTime.Now;
            Generator gen = new Generator(connectionString, databaseName, collectionName, path);
            gen.DoWork();
            DateTime ended = DateTime.Now;
            TimeSpan elapsed = ended - started;
            Console.WriteLine("Elapsed time: " + elapsed.ToString());
            Console.WriteLine(gen.GamesSeen + " games seen.");
            Console.WriteLine(gen.AtomicGames + " were atomic.");
            Console.WriteLine(gen.UniquePositionsAdded + " unique positions added.");
        }
    }
}
