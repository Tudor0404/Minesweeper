using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Minesweeper {

    [Serializable]
    public struct Run {
        private TimeSpan time;
        private DateTime dateStarted;
        private int seed;
        private bool seedWasRandom;
        private string difficulty;

        public TimeSpan Time { get => time; set => time = value; }
        public DateTime DateStarted { get => dateStarted; set => dateStarted = value; }
        public int Seed { get => seed; set => seed = value; }
        public bool SeedWasRandom { get => seedWasRandom; set => seedWasRandom = value; }
        public string Difficulty { get => difficulty; set => difficulty = value; }
        public string TimeFormatted => String.Format("{0:00}:{1:00}:{2:00}", Time.Minutes, Time.Seconds, Time.Milliseconds);
        public string DateStartedFormatted => DateStarted.ToLongDateString();
    }

    internal class PastRuns {
        static public Dictionary<string, List<Run>> runs = new Dictionary<string, List<Run>>();
        static private string dirpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WPFMinesweeper");
        static private string filePath = Path.Combine(dirpath, "pastruns.txt");

        static public List<Run> GetByIndex(int index) {
            if (!runs.ContainsKey(MainWindow.difficulties[index].Item1)) {
                PastRuns.runs[MainWindow.difficulties[index].Item1] = new List<Run>();
            }
            return runs[MainWindow.difficulties[index].Item1];
        }

        static public void LoadRuns() {
            if (File.Exists(filePath)) {
                try {
                    runs = JsonConvert.DeserializeObject<Dictionary<string, List<Run>>>(File.ReadAllText(filePath));
                } catch {
                    File.Create(filePath);
                }
            }
        }

        static public void SaveRuns() {
            if (!Directory.Exists(dirpath)) {
                Directory.CreateDirectory(dirpath);
            }

            string json = JsonConvert.SerializeObject(runs);
            File.WriteAllText(filePath, json);
        }
    }
}