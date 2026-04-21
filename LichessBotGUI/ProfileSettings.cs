using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LichessBotGUI
{
    public class BotProfile
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Default";

        [JsonPropertyName("colorTag")]
        public string ColorTag { get; set; } = "#d4985a";

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; } = false;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("lastUsed")]
        public DateTime? LastUsed { get; set; }

        [JsonPropertyName("apiToken")]
        public string ApiToken { get; set; } = "";

        [JsonPropertyName("autoChallenger")]
        public bool AutoChallenger { get; set; } = true;

        [JsonPropertyName("rated")]
        public bool Rated { get; set; } = false;

        [JsonPropertyName("autoResign")]
        public bool AutoResign { get; set; } = true;

        [JsonPropertyName("resignThreshold")]
        public double ResignThreshold { get; set; } = -5.0;

        [JsonPropertyName("minRating")]
        public int MinRating { get; set; } = 1900;

        [JsonPropertyName("maxGames")]
        public int MaxGames { get; set; } = 0;

        [JsonPropertyName("maxConcurrent")]
        public int MaxConcurrent { get; set; } = 1;

        [JsonPropertyName("acceptRapid")]
        public bool AcceptRapid { get; set; } = false;

        [JsonPropertyName("includeChess960")]
        public bool IncludeChess960 { get; set; } = false;

        [JsonPropertyName("autoOpenGame")]
        public bool AutoOpenGame { get; set; } = false;

        [JsonPropertyName("acceptRematch")]
        public bool AcceptRematch { get; set; } = true;

        [JsonPropertyName("enginePath")]
        public string EnginePath { get; set; } = "Default Stockfish 18";

        [JsonPropertyName("skillLevel")]
        public int SkillLevel { get; set; } = 20;

        [JsonPropertyName("maxDepth")]
        public int MaxDepth { get; set; } = 0;

        [JsonPropertyName("moveSpeed")]
        public double MoveSpeed { get; set; } = 1.0;

        [JsonPropertyName("threads")]
        public int Threads { get; set; } = 0;

        [JsonPropertyName("hashMB")]
        public int HashMB { get; set; } = 256;

        [JsonPropertyName("useNNUE")]
        public bool UseNNUE { get; set; } = true;

        [JsonPropertyName("ponder")]
        public bool Ponder { get; set; } = false;

        [JsonPropertyName("moveOverheadMs")]
        public int MoveOverheadMs { get; set; } = 100;

        [JsonPropertyName("tcMinutes")]
        public double TcMinutes { get; set; } = 2.0;

        [JsonPropertyName("tcIncrement")]
        public int TcIncrement { get; set; } = 1;

        [JsonPropertyName("tcPreset")]
        public string TcPreset { get; set; } = "Blitz";

        [JsonPropertyName("bookPath")]
        public string BookPath { get; set; } = "Default gm_openings.bin";

        [JsonPropertyName("bookEnabled")]
        public bool BookEnabled { get; set; } = true;

        [JsonPropertyName("sendChat")]
        public bool SendChat { get; set; } = true;

        [JsonPropertyName("greeting")]
        public string Greeting { get; set; } = "glhf!";

        [JsonPropertyName("ggMessage")]
        public string GGMessage { get; set; } = "gg wp!";

        public BotProfile Clone()
        {
            return new BotProfile
            {
                Id = Guid.NewGuid(),
                Name = Name,
                ColorTag = ColorTag,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                LastUsed = null,
                ApiToken = ApiToken,
                AutoChallenger = AutoChallenger,
                Rated = Rated,
                AutoResign = AutoResign,
                ResignThreshold = ResignThreshold,
                MinRating = MinRating,
                MaxGames = MaxGames,
                MaxConcurrent = MaxConcurrent,
                AcceptRapid = AcceptRapid,
                IncludeChess960 = IncludeChess960,
                AutoOpenGame = AutoOpenGame,
                AcceptRematch = AcceptRematch,
                EnginePath = EnginePath,
                SkillLevel = SkillLevel,
                MaxDepth = MaxDepth,
                MoveSpeed = MoveSpeed,
                Threads = Threads,
                HashMB = HashMB,
                UseNNUE = UseNNUE,
                Ponder = Ponder,
                MoveOverheadMs = MoveOverheadMs,
                TcMinutes = TcMinutes,
                TcIncrement = TcIncrement,
                TcPreset = TcPreset,
                BookPath = BookPath,
                BookEnabled = BookEnabled,
                SendChat = SendChat,
                Greeting = Greeting,
                GGMessage = GGMessage,
            };
        }
    }

    public class BotProfileStore
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("profiles")]
        public List<BotProfile> Profiles { get; set; } = new();
    }
}
