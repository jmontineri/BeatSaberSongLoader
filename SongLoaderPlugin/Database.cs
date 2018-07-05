using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.Common;
using System.Data.SQLite;
using System.Data.SQLite.Linq;
using System.IO;
using Smash;

namespace SongLoaderPlugin
{
    class Database
    {
        private readonly DbConnection conn;

        public Database(string folderPath)
        {
            DbProviderFactory fact = SQLiteProviderFactory.Instance;
            conn = fact.CreateConnection();
            conn.ConnectionString = "Data Source=" + folderPath + "\\songs.sqlite";
            conn.Open();
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using (DbCommand command = conn.CreateCommand())
            {
                // Massive command to create the tables
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS difficulties (song UNSIGNED BIGINT, audioPath TEXT, difficultyName TEXT, difficultyRank real, fileName text, audioOffset real, oldOffset real, primary key(song, fileName)); CREATE TABLE IF NOT EXISTS songs(hash UNSIGNED BIGINT PRIMARY KEY, leaderboardid TEXT, beatsaverid INT, bpm REAL, previewStartTime REAL, previewDuration REAL, directory TEXT, authorName TEXT, songName TEXT, songSubname TEXT, coverImagePath TEXT, environmentName TEXT); ";
                command.ExecuteNonQuery();
            }
        }

        public List<CustomSongInfo> GetSongs()
        {
            try
            {
                return GetSongs("ORDER BY upper(songName);");
            }
            catch (Exception e)
            {
                Logger.Log(e.ToString());
            }
            return null;
        }

        public List<CustomSongInfo> GetSongs(string filter)
        {
            List<CustomSongInfo> ret = new List<CustomSongInfo>();
            using (DbTransaction tx = conn.BeginTransaction())
            {
                Logger.Log("Getting songs");
                DbCommand getSongs = conn.CreateCommand();
                getSongs.CommandText = "SELECT * FROM songs " + filter;
                DbDataReader reader = getSongs.ExecuteReader();
                while (reader.Read())
                {
                    Logger.Log(reader.GetString(7));
                    CustomSongInfo info = new CustomSongInfo()
                    {
                        difficultyLevels = GetDiffs((ulong)reader.GetInt64(0)).ToArray(),
                        levelId = reader.GetString(1),
                        beatsaverId = reader.GetInt32(2),
                        beatsPerMinute = reader.GetFloat(3),
                        previewStartTime = reader.GetFloat(4),
                        previewDuration = reader.GetFloat(5),
                        path = reader.GetString(6),
                        authorName = reader.GetString(7),
                        songName = reader.GetString(8),
                        songSubName = reader.GetString(9),
                        coverImagePath = reader.GetString(10),
                        environmentName = reader.GetString(11)
                    };
                    foreach (CustomSongInfo.DifficultyLevel infoDifficultyLevel in info.difficultyLevels)
                    {
                        Logger.Log(infoDifficultyLevel.difficulty);
                    }
                    ret.Add(info);
                }
            }
            return ret;
        }

        private List<CustomSongInfo.DifficultyLevel> GetDiffs(ulong hash)
        {
            List<CustomSongInfo.DifficultyLevel> ret = new List<CustomSongInfo.DifficultyLevel>();
            DbCommand getDiffs = conn.CreateCommand();
            getDiffs.CommandText =
                "SELECT * FROM difficulties WHERE song=@hash ORDER BY case difficultyName when \"Easy\" then 1 When \"Normal\" then 2 When \"Hard\" then 3 When \"Expert\" then 4 else 5 end;";
            getDiffs.Parameters.Add(new SQLiteParameter("hash", hash));

            DbDataReader reader = getDiffs.ExecuteReader();
            while (reader.Read())
            {
                ret.Add(new CustomSongInfo.DifficultyLevel()
                {
                    audioPath = reader.GetString(1),
                    difficulty = reader.GetString(2),
                    difficultyRank = (int)reader.GetFloat(3),
                    jsonPath = reader.GetString(4)
                });

            }
            return ret;
        }

        public void UpdateSongDB(string[] folderNames, bool fullRescan)
        {
            List<ulong> hashes = new List<ulong>();
            xxHash.Hash64 hasher = xxHash.Create64(0);

            using (DbTransaction tx = conn.BeginTransaction())
            {
                DbCommand checkCommand = conn.CreateCommand();
                DbCommand updateCommand = conn.CreateCommand();
                DbCommand updateDiffCommand = conn.CreateCommand();

                #region sqlite parameters
                SQLiteParameter hashParam = new SQLiteParameter("@hash");
                SQLiteParameter directoryParam = new SQLiteParameter("@directory");

                SQLiteParameter beatsaveridParam = new SQLiteParameter("@beatsaverid");
                SQLiteParameter leaderboardidParam = new SQLiteParameter("@leaderboardid");
                SQLiteParameter bpmParam = new SQLiteParameter("@bpm");
                SQLiteParameter previewStartTimeParam = new SQLiteParameter("@previewStartTime");
                SQLiteParameter previewDurationParam = new SQLiteParameter("@previewDuration");
                SQLiteParameter authorNameParam = new SQLiteParameter("@authorName");
                SQLiteParameter songNameParam = new SQLiteParameter("@songName");
                SQLiteParameter songSubnameParam = new SQLiteParameter("@songSubname");
                SQLiteParameter coverImagePathParam = new SQLiteParameter("@coverImagePath");
                SQLiteParameter environmentNameParam = new SQLiteParameter("@environmentName");

                SQLiteParameter audioPathParam = new SQLiteParameter("@audioPath");
                SQLiteParameter difficultyNameParam = new SQLiteParameter("@difficultyName");
                SQLiteParameter difficultyRankParam = new SQLiteParameter("@difficultyRank");
                SQLiteParameter fileNameParam = new SQLiteParameter("@fileName");

                checkCommand.Parameters.Add(hashParam);
                checkCommand.Parameters.Add(directoryParam);

                updateCommand.Parameters.Add(hashParam);
                updateCommand.Parameters.Add(directoryParam);
                updateCommand.Parameters.Add(beatsaveridParam);
                updateCommand.Parameters.Add(leaderboardidParam);
                updateCommand.Parameters.Add(bpmParam);
                updateCommand.Parameters.Add(previewStartTimeParam);
                updateCommand.Parameters.Add(previewDurationParam);
                updateCommand.Parameters.Add(authorNameParam);
                updateCommand.Parameters.Add(songNameParam);
                updateCommand.Parameters.Add(songSubnameParam);
                updateCommand.Parameters.Add(coverImagePathParam);
                updateCommand.Parameters.Add(environmentNameParam);

                updateDiffCommand.Parameters.Add(hashParam);
                updateDiffCommand.Parameters.Add(directoryParam);
                updateDiffCommand.Parameters.Add(audioPathParam);
                updateDiffCommand.Parameters.Add(difficultyNameParam);
                updateDiffCommand.Parameters.Add(difficultyRankParam);
                updateDiffCommand.Parameters.Add(fileNameParam);
                #endregion

                // Query to check if song exists and is in right location
                checkCommand.CommandText = "select count(hash) from songs where hash = @hash and directory = @directory";

                // Query to add a song into the database if it isn't there yet
                updateCommand.CommandText = "update songs set beatsaverid=@beatsaverid, leaderboardid=@leaderboardid, bpm=@bpm, previewStartTime=@previewStartTime, previewDuration=@previewDuration, directory=@directory, authorName=@authorName, songName=@songName, songSubname=@songSubname, coverImagePath=@coverImagePath, environmentName=@environmentName where hash=@hash; insert or ignore into songs (hash, beatsaverid, leaderboardid, bpm, previewStartTime, previewDuration, directory, authorName, songName, songSubname, coverImagePath, environmentName) values(@hash, @beatsaverid, @leaderboardid, @bpm, @previewStartTime, @previewDuration, @directory, @authorName, @songName, @songSubname, @coverImagePath, @environmentName);";

                // Query to add or update difficulty files
                updateDiffCommand.CommandText = "update difficulties set audioPath = @audioPath, difficultyName = @difficultyName, difficultyRank = @difficultyRank where fileName = @fileName and song = @hash; insert or ignore into difficulties (song, audioPath, difficultyName, difficultyRank, fileName) values (@hash, @audioPath, @difficultyName, @difficultyRank, @fileName)";

                foreach (string folder in folderNames)
                {
                    // Hash info.json file to use as a key in the db
                    hasher.Write(File.ReadAllText(folder + "/info.json"));
                    ulong currentHash = hasher.Compute();
                    hasher.Reset(0);

                    // Ignore duplicates
                    if (hashes.Contains(currentHash))
                    {
                        Logger.Log("The song in directory " + folder + " has a duplicate hash " + currentHash + " and was ignored");
                        continue;
                    }
                    hashes.Add(currentHash);

                    directoryParam.Value = folder;
                    hashParam.Value = currentHash;

                    if (fullRescan || (Int64)checkCommand.ExecuteScalar() == 0)
                    {
                        Logger.Log("Song is not in DB or has changed, adding or updating");

                        // We need to update the database entry for the current song
                        CustomSongInfo songInfo = CustomSongInfo.FromPath(folder);
                        beatsaveridParam.Value = 0; //TODO
                        leaderboardidParam.Value = songInfo.GetIdentifier();
                        bpmParam.Value = songInfo.beatsPerMinute;
                        previewStartTimeParam.Value = songInfo.previewStartTime;
                        previewDurationParam.Value = songInfo.previewDuration;
                        authorNameParam.Value = songInfo.authorName;
                        songNameParam.Value = songInfo.songName;
                        songSubnameParam.Value = songInfo.songSubName;
                        coverImagePathParam.Value = songInfo.coverImagePath;
                        environmentNameParam.Value = songInfo.environmentName;

                        updateCommand.ExecuteNonQuery();

                        // And add all the difficulties into the database
                        foreach (CustomSongInfo.DifficultyLevel diff in songInfo.difficultyLevels)
                        {
                            difficultyNameParam.Value = diff.difficulty;
                            difficultyRankParam.Value = diff.difficultyRank;
                            audioPathParam.Value = diff.audioPath;
                            fileNameParam.Value = diff.jsonPath;

                            updateDiffCommand.ExecuteNonQuery();
                        }

                    }
                }

                DbCommand deleteUnusedCommand = conn.CreateCommand();
                StringBuilder sb = new StringBuilder();
                foreach (string folderName in folderNames)
                {
                    sb.Append("\"" + folderName + "\", ");
                }
                sb.Remove(sb.Length - 2, 2);
                deleteUnusedCommand.CommandText =
                    string.Format("SELECT COUNT(hash) FROM songs WHERE directory NOT IN ({0}); DELETE from difficulties where song in (SELECT hash from songs where directory not in ({0})); delete from songs where directory not in ({0});", sb.ToString());
                deleteUnusedCommand.Parameters.Add(new SQLiteParameter("@array", folderNames));
                object result = deleteUnusedCommand.ExecuteScalar();
                Logger.Log("Removed " + result.ToString() + " deleted/moved songs");
                tx.Commit();
            }
        }
    }
}
