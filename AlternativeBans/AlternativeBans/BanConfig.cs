﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace AlternativeBans
{
    public class BanConfig
    {
        public bool UseDimensions { get; set; }

        public bool DisableConvertCommand { get; set; }

        public bool DeleteExpiredBansOnServerStart { get; set; } = true;

        public bool DeleteExpiredBansFoundInListCommand { get; set; } = true;

        public int MaxBansPerPageIngame { get; set; } = 8;

        public int MaxBansPerPageDiscord { get; set; } = 100;

        public List<string> BannableDimensions { get; set; }

        public string DimensionName { get; set; } = "server";

        public string dontchangethis = "Possible values for DefaultBanType: all (ban all identifiers), uuid, ip, name or account";
        public string DefaultBanType { get; set; } = "all";

        public string SQLHost { get; set; } = "localhost:3306";

        public string SQLDatabaseName { get; set; } = "global";

        public string SQLUsername { get; set; }

        public string SQLPassword { get; set; }

        private static readonly string _savePath = Path.Combine(TShockAPI.TShock.SavePath, "altbanscfg.json");

        public static BanConfig Read()
        {
            try
            {
                if (!File.Exists(_savePath))
                    File.WriteAllText(_savePath, JsonConvert.SerializeObject(new BanConfig() { BannableDimensions = new List<string>() { "all" } }));

                var res = JsonConvert.DeserializeObject<BanConfig>(File.ReadAllText(_savePath));
                File.WriteAllText(_savePath, JsonConvert.SerializeObject(res, Formatting.Indented));
                return res;
            }
            catch (Exception ex)
            {
                TShockAPI.TShock.Log.ConsoleError(ex.ToString());
            }

            return new BanConfig();
        }
    }
}
