﻿using MongoDB.Bson.Serialization.Attributes;

namespace PositionGenerator
{
    class TrainingPosition
    {
        [BsonElement("_id")]
        public string ID { get; set; }

        [BsonElement("type")]
        public string Type { get; set; }

        [BsonElement("fen")]
        public string FEN { get; set; }

        [BsonElement("location")]
        public double[] Location { get; set; }
        // The purpose of 'Location' on training positions, is to be able to use MongoDB's 'near' filter to find a random puzzle; there is no built-in filter for a random document.

        [BsonElement("lastMove")]
        public string[] LastMove { get; set; }

        [BsonElement("variant")]
        public string Variant { get; set; }
    }
}
