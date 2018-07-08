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
    public class Database
    {
        #region Private fields

        private readonly DbConnection conn;
        private DbCommand checkCommand;
        private DbCommand updateCommand;
        private DbCommand updateIdCommand;
        private DbCommand updateDiffCommand;

        private SQLiteParameter directoryParam;
        private SQLiteParameter hashParam;
        private SQLiteParameter beatsaveridParam;
        private SQLiteParameter newidParam;
        private SQLiteParameter leaderboardidParam;
        private SQLiteParameter bpmParam;
        private SQLiteParameter previewStartTimeParam;
        private SQLiteParameter previewDurationParam;
        private SQLiteParameter authorNameParam;
        private SQLiteParameter songNameParam;
        private SQLiteParameter songSubnameParam;
        private SQLiteParameter coverImagePathParam;
        private SQLiteParameter environmentNameParam;
        private SQLiteParameter audioPathParam;
        private SQLiteParameter difficultyNameParam;
        private SQLiteParameter difficultyRankParam;
        private SQLiteParameter fileNameParam;

        #endregion

        public Database(string folderPath)
        {
            DbProviderFactory fact = SQLiteProviderFactory.Instance;
            conn = fact.CreateConnection();
            conn.ConnectionString = "Data Source=" + folderPath + "\\songs.sqlite";
            conn.Open();
            InitializeDatabase();
            SetupCommands();
        }

        public List<CustomSongInfo> GetSongs()
        {
            return GetSongs("ORDER BY upper(songName);");
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
                    Logger.Log(reader.GetString(6));
                    CustomSongInfo info = new CustomSongInfo()
                    {
                        difficultyLevels = GetDiffs((ulong) reader.GetInt64(0)).ToArray(),
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

        public void UpdateSongDB(string[] folderNames, bool fullRescan)
        {
            List<ulong> hashes = new List<ulong>();
            xxHash.Hash64 hasher = xxHash.Create64(0);

            using (DbTransaction tx = conn.BeginTransaction())
            {
                foreach (string folder in folderNames)
                {
                    AddSong(folder, hasher, hashes, fullRescan);
                }

                DbCommand deleteUnusedCommand = conn.CreateCommand();
                StringBuilder sb = new StringBuilder();
                foreach (string folderName in folderNames)
                {
                    sb.Append("\"" + folderName + "\", ");
                }
                sb.Remove(sb.Length - 2, 2);
                deleteUnusedCommand.CommandText =
                    string.Format(
                        "SELECT COUNT(hash) FROM songs WHERE directory NOT IN ({0}); DELETE from difficulties where song in (SELECT hash from songs where directory not in ({0})); delete from songs where directory not in ({0});",
                        sb.ToString());
                deleteUnusedCommand.Parameters.Add(new SQLiteParameter("@array", folderNames));
                object result = deleteUnusedCommand.ExecuteScalar();
                Logger.Log("Removed " + result.ToString() + " deleted/moved songs");
                tx.Commit();
            }
        }

        public void AddSong(string folder, bool forceAdd)
        {
            AddSong(folder, xxHash.Create64(0), new List<ulong>(), forceAdd);
        }

        public void UpdateSongID(string songLevelId, string newId)
        {
            leaderboardidParam.Value = songLevelId;
            newidParam.Value = newId;

            updateIdCommand.ExecuteNonQuery();
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

        private void SetupCommands()
        {
            checkCommand = conn.CreateCommand();
            updateCommand = conn.CreateCommand();
            updateDiffCommand = conn.CreateCommand();
            updateIdCommand = conn.CreateCommand();

            #region sqlite parameters

            hashParam = new SQLiteParameter("@hash");
            directoryParam = new SQLiteParameter("@directory");

            beatsaveridParam = new SQLiteParameter("@beatsaverid");
            leaderboardidParam = new SQLiteParameter("@leaderboardid");
            newidParam = new SQLiteParameter("@newid");
            bpmParam = new SQLiteParameter("@bpm");
            previewStartTimeParam = new SQLiteParameter("@previewStartTime");
            previewDurationParam = new SQLiteParameter("@previewDuration");
            authorNameParam = new SQLiteParameter("@authorName");
            songNameParam = new SQLiteParameter("@songName");
            songSubnameParam = new SQLiteParameter("@songSubname");
            coverImagePathParam = new SQLiteParameter("@coverImagePath");
            environmentNameParam = new SQLiteParameter("@environmentName");

            audioPathParam = new SQLiteParameter("@audioPath");
            difficultyNameParam = new SQLiteParameter("@difficultyName");
            difficultyRankParam = new SQLiteParameter("@difficultyRank");
            fileNameParam = new SQLiteParameter("@fileName");

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

            updateIdCommand.Parameters.Add(leaderboardidParam);
            updateIdCommand.Parameters.Add(newidParam);

            #endregion

            // Query to check if song exists and is in right location
            checkCommand.CommandText = "select count(hash) from songs where hash = @hash and directory = @directory";

            // Query to add a song into the database if it isn't there yet
            updateCommand.CommandText =
                "update songs set beatsaverid=@beatsaverid, leaderboardid=@leaderboardid, bpm=@bpm, previewStartTime=@previewStartTime, previewDuration=@previewDuration, directory=@directory, authorName=@authorName, songName=@songName, songSubname=@songSubname, coverImagePath=@coverImagePath, environmentName=@environmentName where hash=@hash; insert or ignore into songs (hash, beatsaverid, leaderboardid, bpm, previewStartTime, previewDuration, directory, authorName, songName, songSubname, coverImagePath, environmentName) values(@hash, @beatsaverid, @leaderboardid, @bpm, @previewStartTime, @previewDuration, @directory, @authorName, @songName, @songSubname, @coverImagePath, @environmentName);";

            // Query to add or update difficulty files
            updateDiffCommand.CommandText =
                "update difficulties set audioPath = @audioPath, difficultyName = @difficultyName, difficultyRank = @difficultyRank where fileName = @fileName and song = @hash; insert or ignore into difficulties (song, audioPath, difficultyName, difficultyRank, fileName) values (@hash, @audioPath, @difficultyName, @difficultyRank, @fileName)";

            // Query to update a leaderboard ID after the song has been edited
            updateIdCommand.CommandText =
                "update songs set leaderboardid = @newid where leaderboardid = @leaderboardid;";
        }

        private List<CustomSongInfo.DifficultyLevel> GetDiffs(ulong hash)
        {
            List<CustomSongInfo.DifficultyLevel> ret = new List<CustomSongInfo.DifficultyLevel>();
            DbCommand getDiffs = conn.CreateCommand();
            getDiffs.CommandText =
                "SELECT * FROM difficulties WHERE song=@hash ORDER BY case difficultyName when \"Easy\" then 1 When \"Normal\" then 2 When \"Hard\" then 3 When \"Expert\" then 4 else 5 end;";
            getDiffs.Parameters.Add(new SQLiteParameter("hash", hash));

            DbDataReader reader = getDiffs.ExecuteReader();
            int i = 0;
            while (reader.Read())
            {
                ret.Add(new CustomSongInfo.DifficultyLevel()
                {
                    audioPath = reader.GetString(1),
                    difficulty = reader.GetString(2),
                    difficultyRank = i,
                    jsonPath = reader.GetString(4)
                });

                i++;
            }
            return ret;
        }

        private void AddSong(string folder, xxHash.Hash64 hasher, List<ulong> scannedHashes, bool forceAdd)
        {
            // Hash info.json file to use as a key in the db
            hasher.Write(File.ReadAllText(folder + "/info.json"));
            ulong currentHash = hasher.Compute();
            hasher.Reset(0);

            // Ignore duplicates
            if (scannedHashes.Contains(currentHash))
            {
                Logger.Log("The song in directory " + folder + " has a duplicate hash " + currentHash +
                           " and was ignored");
                return;
            }
            scannedHashes.Add(currentHash);

            directoryParam.Value = folder;
            hashParam.Value = currentHash;

            if (forceAdd || (Int64) checkCommand.ExecuteScalar() == 0)
            {
                Logger.Log("Song is not in DB or has changed, adding or updating");

                // We need to update the database entry for the current song
                CustomSongInfo songInfo = CustomSongInfo.FromPath(folder);
                beatsaveridParam.Value = 0; //TODO
                leaderboardidParam.Value = songInfo.GetIdentifier();
                bpmParam.Value = songInfo.beatsPerMinute;
                previewStartTimeParam.Value = songInfo.previewStartTime;
                previewDurationParam.Value = songInfo.previewDuration;
                authorNameParam.Value = songInfo.authorName ?? "";
                songNameParam.Value = songInfo.songName ?? "";
                songSubnameParam.Value = songInfo.songSubName ?? "";
                coverImagePathParam.Value = songInfo.coverImagePath ?? "";
                environmentNameParam.Value = songInfo.environmentName ?? "";

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
    }
}