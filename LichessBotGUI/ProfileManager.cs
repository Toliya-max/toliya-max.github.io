using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LichessBotGUI
{
    public static class ProfileManager
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static string ProfilesPath(string botDirectory) =>
            Path.Combine(botDirectory, "bot_profiles.json");

        public static List<BotProfile> Load(string botDirectory)
        {
            string path = ProfilesPath(botDirectory);
            if (!File.Exists(path))
                return new List<BotProfile>();

            try
            {
                string json = File.ReadAllText(path);
                var store = JsonSerializer.Deserialize<BotProfileStore>(json, JsonOpts);
                if (store?.Profiles == null || store.Profiles.Count == 0)
                    return new List<BotProfile>();

                var profiles = store.Profiles;

                var active = profiles.FirstOrDefault(p => p.IsActive);
                if (active == null)
                    profiles[0].IsActive = true;

                return profiles;
            }
            catch
            {
                return new List<BotProfile>();
            }
        }

        public static void Save(string botDirectory, List<BotProfile> profiles)
        {
            var hasActive = profiles.Any(p => p.IsActive);
            if (!hasActive && profiles.Count > 0)
                profiles[0].IsActive = true;

            var store = new BotProfileStore { Version = 1, Profiles = profiles };
            string json = JsonSerializer.Serialize(store, JsonOpts);
            File.WriteAllText(ProfilesPath(botDirectory), json);
        }

        public static BotProfile? GetActive(string botDirectory)
        {
            var profiles = Load(botDirectory);
            return profiles.FirstOrDefault(p => p.IsActive) ?? profiles.FirstOrDefault();
        }

        public static void SetActive(string botDirectory, Guid id)
        {
            var profiles = Load(botDirectory);
            foreach (var p in profiles)
                p.IsActive = p.Id == id;
            Save(botDirectory, profiles);
        }

        public static void Export(BotProfile profile, string path)
        {
            var store = new BotProfileStore { Version = 1, Profiles = new List<BotProfile> { profile } };
            string json = JsonSerializer.Serialize(store, JsonOpts);
            File.WriteAllText(path, json);
        }

        public static BotProfile? Import(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var store = JsonSerializer.Deserialize<BotProfileStore>(json, JsonOpts);
                var profile = store?.Profiles?.FirstOrDefault();
                if (profile == null) return null;
                profile.Id = Guid.NewGuid();
                profile.IsActive = false;
                return profile;
            }
            catch
            {
                return null;
            }
        }

        public static bool MigrateFromLegacy(string botDirectory)
        {
            string profilesPath = ProfilesPath(botDirectory);
            string settingsPath = Path.Combine(botDirectory, "settings.json");
            string settingsBackup = Path.Combine(botDirectory, "settings.json.bak");

            if (File.Exists(profilesPath))
                return false;
            if (!File.Exists(settingsPath))
                return false;

            try
            {
                string json = File.ReadAllText(settingsPath);
                var legacyOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var settings = JsonSerializer.Deserialize<BotSettings>(json, legacyOpts)
                    ?? JsonSerializer.Deserialize<BotSettings>(json)
                    ?? new BotSettings();

                var profile = new BotProfile
                {
                    Id = Guid.NewGuid(),
                    Name = "Default",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    AutoChallenger = settings.AutoChallenger,
                    Rated = settings.Rated,
                    AutoResign = settings.AutoResign,
                    ResignThreshold = double.TryParse(settings.ResignThreshold,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double rt) ? rt : -5.0,
                    MinRating = int.TryParse(settings.MinRating, out int mr) ? mr : 1900,
                    MaxGames = int.TryParse(settings.MaxGames, out int mg) ? mg : 0,
                    TcMinutes = double.TryParse(settings.BaseTime,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double tc) ? tc : 3.0,
                    TcIncrement = int.TryParse(settings.Increment, out int inc) ? inc : 0,
                    EnginePath = settings.EnginePath ?? "Default Stockfish 18",
                    BookPath = settings.BookPath ?? "Default gm_openings.bin",
                    UseNNUE = settings.UseNNUE,
                    SkillLevel = (int)settings.SkillLevel,
                    MoveSpeed = settings.MoveSpeed,
                    MaxDepth = int.TryParse(settings.MaxDepth, out int md) ? md : 0,
                    Threads = settings.Threads,
                    HashMB = settings.Hash,
                    MoveOverheadMs = int.TryParse(settings.MoveOverhead, out int mo) ? mo : 100,
                    SendChat = settings.SendChat,
                    Greeting = settings.Greeting ?? "glhf!",
                    GGMessage = settings.GGMessage ?? "gg wp!",
                    AcceptRematch = settings.AcceptRematch,
                    ApiToken = ReadTokenFromEnv(botDirectory),
                };

                DerivePreset(profile);
                Save(botDirectory, new List<BotProfile> { profile });

                try { File.Move(settingsPath, settingsBackup, overwrite: true); } catch { }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadTokenFromEnv(string botDirectory)
        {
            string envPath = Path.Combine(botDirectory, ".env");
            if (!File.Exists(envPath)) return "";
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (line.StartsWith("LICHESS_API_TOKEN="))
                    return line["LICHESS_API_TOKEN=".Length..].Trim(' ', '"', '\'');
            }
            return "";
        }

        private static void DerivePreset(BotProfile p)
        {
            string mins = p.TcMinutes.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            p.TcPreset = (mins, p.TcIncrement) switch
            {
                ("0.5", 0) => "Hyper",
                ("1", 0) => "Bullet",
                ("3", 0) => "Blitz",
                ("10", 0) => "Rapid",
                ("15", 10) => "Classical",
                _ => "Custom",
            };
        }
    }
}
