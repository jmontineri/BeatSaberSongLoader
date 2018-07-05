using SimpleJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace SongLoaderPlugin
{
	[Serializable]
	public class CustomSongInfo : IEquatable<CustomSongInfo>
	{
		public string songName;
		public string songSubName;
		public string authorName;
		public float beatsPerMinute;
		public float previewStartTime;
		public float previewDuration;
	    public float beatsaverId;
		public string environmentName;
		public string coverImagePath;
		public string videoPath;
		public DifficultyLevel[] difficultyLevels;
		public string path;
		public string levelId;

		[Serializable]
		public class DifficultyLevel
		{
			public string difficulty;
			public int difficultyRank;
			public string audioPath;
			public string jsonPath;
			public string json;
		}

		public string GetIdentifier()
		{
			string combinedJson = "";
			foreach (DifficultyLevel diffLevel in difficultyLevels)
			{
				if (!File.Exists(path + "/" + diffLevel.jsonPath))
				{
					continue;
				}
				
				diffLevel.json = File.ReadAllText(path + "/" + diffLevel.jsonPath);
				combinedJson += diffLevel.json;
			}

			string hash = Utils.CreateMD5FromString(combinedJson);
			return hash + "∎" + string.Join("∎", new[] {songName, songSubName, authorName, beatsPerMinute.ToString()}) + "∎";
		}

        public bool Equals(CustomSongInfo other)
        {
            return levelId == other.levelId;
        }

        public static CustomSongInfo FromPath(string songPath)
        {
            string infoText = File.ReadAllText(songPath + "/info.json");
            CustomSongInfo songInfo;
            try
            {
                songInfo = JsonUtility.FromJson<CustomSongInfo>(infoText);
            }
            catch (Exception)
            {
                Logger.Log("Error parsing song: " + songPath);
                return null;
            }

            songInfo.path = songPath;

            //Here comes SimpleJSON to the rescue when JSONUtility can't handle an array.
            List<DifficultyLevel> diffLevels = new List<CustomSongInfo.DifficultyLevel>();
            JSONNode n = JSON.Parse(infoText);
            JSONNode diffs = n["difficultyLevels"];
            for (int i = 0; i < diffs.AsArray.Count; i++)
            {
                n = diffs[i];
                diffLevels.Add(new CustomSongInfo.DifficultyLevel()
                {
                    difficulty = n["difficulty"],
                    difficultyRank = n["difficultyRank"].AsInt,
                    audioPath = n["audioPath"],
                    jsonPath = n["jsonPath"]
                });
            }

            songInfo.difficultyLevels = diffLevels.ToArray();
            songInfo.levelId = songInfo.GetIdentifier();
            return songInfo;
        }
    }
}
