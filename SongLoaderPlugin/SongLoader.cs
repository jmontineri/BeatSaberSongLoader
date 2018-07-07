using UnityEngine;
using System.Linq;
using System;
using System.CodeDom;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using SongLoaderPlugin.Internals;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace SongLoaderPlugin
{
    public class SongLoader : MonoBehaviour
    {
        public static readonly UnityEvent SongsLoaded = new UnityEvent();
        public static readonly List<CustomSongInfo> CustomSongInfos = new List<CustomSongInfo>();
        public static readonly List<CustomLevelStaticData> CustomLevelStaticDatas = new List<CustomLevelStaticData>();
        public static readonly List<LevelStaticData> OriginalLevelStaticDatas = new List<LevelStaticData>();

        public const int MenuIndex = 1;

        private LeaderboardScoreUploader _leaderboardScoreUploader;
        private SongSelectionMasterViewController _songSelectionView;
        private DifficultyViewController _difficultyView;

        private Database _database;
        private LevelStaticData[] _levels;

        public static void OnLoad()
        {
            if (Instance != null) return;
            new GameObject("Song Loader").AddComponent<SongLoader>();
        }

        public static SongLoader Instance;

        private void Awake()
        {
            Instance = this;

            bool shouldScan = !File.Exists(Environment.CurrentDirectory + "\\songs.sqlite");
            _database = new Database(Environment.CurrentDirectory);
            if (shouldScan)
            {
                Logger.Log("First run, building database");
                _database.UpdateSongDB(GetAllSongFolders().ToArray(), true);
            }
            RefreshSongs();
            SceneManager.activeSceneChanged += SceneManagerOnActiveSceneChanged;
            SceneManagerOnActiveSceneChanged(new Scene(), new Scene());

            DontDestroyOnLoad(gameObject);
        }

        private void SceneManagerOnActiveSceneChanged(Scene arg0, Scene scene)
        {
            StartCoroutine(WaitRemoveScores());

            SongListViewController songListController = Resources.FindObjectsOfTypeAll<SongListViewController>().FirstOrDefault();
            if (songListController == null) return;
            songListController.didSelectSongEvent += OnDidSelectSongEvent;

            _songSelectionView = Resources.FindObjectsOfTypeAll<SongSelectionMasterViewController>().FirstOrDefault();
            _difficultyView = Resources.FindObjectsOfTypeAll<DifficultyViewController>().FirstOrDefault();
        }


        private IEnumerator WaitRemoveScores()
        {
            yield return new WaitForSecondsRealtime(1f);
            RemoveCustomScores();
        }

        //To fix the bug explained in CustomLevelStaticData.cs
        private void OnDidSelectSongEvent(SongListViewController songListViewController)
        {
            try
            {
                CustomLevelStaticData song = CustomLevelStaticDatas.FirstOrDefault(x => x.levelId == songListViewController.levelId);
                if (song == null) return;

                LoadIfNotLoaded(song);

                if (song.difficultyLevels.All(x => x.difficulty != _songSelectionView.difficulty))
                {
                    bool isDiffSelected =
                        ReflectionUtil.GetPrivateField<bool>(_difficultyView, "_difficultySelected");
                    if (!isDiffSelected) return;
                    //The new selected song does not have the current difficulty selected
                    LevelStaticData.DifficultyLevel firstDiff = song.difficultyLevels.FirstOrDefault();
                    if (firstDiff == null) return;
                    ReflectionUtil.SetPrivateField(_songSelectionView, "_difficulty", firstDiff.difficulty);
                }
            }
            catch (Exception e)
            {
                Logger.Log(e.ToString());
            }
        }

        public void LoadIfNotLoaded(CustomLevelStaticData song)
        {
            if (!song.wasLoaded)
            {
                CustomSongInfo info = CustomSongInfo.FromPath(song.jsonPath);
                if (info.GetIdentifier() != song.levelId)
                {
                    Logger.Log("The song data doesn't match, please regenerate the database");
                    throw new Exception("Song was modified");
                }

                foreach (CustomLevelStaticData.CustomDifficultyLevel difficultyLevel in song.difficultyLevels)
                {
                    StartCoroutine(LoadAudio("file://" + difficultyLevel.audioPath, difficultyLevel,
                        "_audioClip"));
                    ReflectionUtil.SetPrivateField(difficultyLevel, "_songLevelData",
                        ParseDifficulty(difficultyLevel.jsonPath));
                }
                song.wasLoaded = true;
            }
        }

        private List<CustomSongInfo> FilterByPlaylist(List<CustomSongInfo> songList, List<string> playlist)
        {
            List<CustomSongInfo> filteredList = new List<CustomSongInfo>();
            foreach (CustomSongInfo song in songList)
            {
                Logger.Log(song.songName);
                if (playlist.Contains(song.songName))
                {
                    Logger.Log("OK: song.songName");
                    filteredList.Add(song);
                }
            }

            return filteredList.OrderBy(song =>
            {
                string name = song.songName;
                for (int i = 0; i < playlist.Count; i++)
                {
                    if (name == playlist[i])
                        return i;
                }
                return 0;
            }).ToList();
        }

        private List<string> GetPlaylist()
        {
            string[] playlistFiles = Directory.GetFiles(Environment.CurrentDirectory + "/CustomSongs/", "playlist.json", SearchOption.TopDirectoryOnly);
            if (playlistFiles.Length == 0)
            {
                return null;
            }
            else
            {
                string playlistString = File.ReadAllText(playlistFiles[0]); // For now only support 1 playlist, this will change
                JSONNode playlist;
                try
                {
                    playlist = JSON.Parse(playlistString)["songs"];
                    List<string> ret = new List<string>();
                    foreach (JSONNode node in playlist.AsArray)
                    {
                        ret.Add(node["songName"].Value);
                    }
                    return ret;
                }
                catch (Exception)
                {
                    Logger.Log("Error parsing playlist file: " + playlistFiles[0]);
                    return null;
                }
            }
        }

        // Parameterless call to not break other mods
        public void RefreshSongs()
        {
            RefreshSongs(false);
        }

        public void RefreshSongs(bool fullRefresh)
        {
            if (SceneManager.GetActiveScene().buildIndex != MenuIndex) return;

            List<CustomSongInfo> songs = _database.GetSongs();
            List<string> playlist = GetPlaylist(); // TODO: Put this in DB
            List<LevelStaticData> newLevelData;

            GameScenesManager gameScenesManager =
                Resources.FindObjectsOfTypeAll<GameScenesManager>().FirstOrDefault();
            GameDataModel gameDataModel = PersistentSingleton<GameDataModel>.instance;

            if (OriginalLevelStaticDatas.Count == 0)
            {
                foreach (LevelStaticData level in gameDataModel.gameStaticData.worldsData[0].levelsData
                    .ToList())
                {
                    OriginalLevelStaticDatas.Add(level);
                }
            }

            if (playlist == null)
            {
                newLevelData = new List<LevelStaticData>(OriginalLevelStaticDatas);
            }
            else
            {
                songs = FilterByPlaylist(songs, playlist);
                newLevelData = new List<LevelStaticData>();
            }

            if (fullRefresh)
            {
                CustomLevelStaticDatas.Clear();
                CustomSongInfos.Clear();
            }
            else
            {
                int beforeCount = CustomLevelStaticDatas.Count;
                CustomSongInfos.RemoveAll(x => !songs.Contains(x));
                CustomLevelStaticDatas.RemoveAll(x => !songs.Any(y => y.levelId == x.levelId));

                // Check why info and data are not the same
                if (CustomSongInfos.Count != CustomLevelStaticDatas.Count)
                {
                    foreach (CustomSongInfo song in CustomSongInfos)
                    {
                        if (!CustomLevelStaticDatas.Any(x => x.levelId == song.levelId))
                        {
                            Logger.Log("Song at " + song.path + " has some issue");
                        }
                    }
                }
            }

            Resources.UnloadUnusedAssets();

            foreach (CustomSongInfo song in songs)
            {
                // Add existing copy of song
                if (CustomSongInfos.Contains(song))
                {
                    foreach (CustomLevelStaticData level in CustomLevelStaticDatas)
                    {
                        if (level.levelId == song.levelId)
                        {
                            //Logger.Log("Readded: " + song.songName);
                            newLevelData.Add(level);
                            break;
                        }
                    }
                    continue;
                }

                // Add new songs
                //Logger.Log("New song found: " + song.songName);
                CustomLevelStaticData newLevel = LoadNewSong(song, gameScenesManager);
                if (newLevel != null)
                {
                    CustomSongInfos.Add(song);
                    newLevel.OnEnable();
                    newLevelData.Add(newLevel);
                    CustomLevelStaticDatas.Add(newLevel);
                }
            }

            _levels = newLevelData.ToArray();
            ReflectionUtil.SetPrivateField(gameDataModel.gameStaticData.worldsData[0], "_levelsData", _levels);
            SongsLoaded.Invoke();
        }

        private CustomLevelStaticData LoadNewSong(CustomSongInfo song, GameScenesManager gameScenesManager)
        {
            CustomLevelStaticData newLevel = null;
            try
            {
                newLevel = ScriptableObject.CreateInstance<CustomLevelStaticData>();
                newLevel.jsonPath = song.path;
            }
            catch (NullReferenceException)
            {
                //LevelStaticData.OnEnable throws null reference exception because we don't have time to set _difficultyLevels
            }

            ReflectionUtil.SetPrivateField(newLevel, "_levelId", song.levelId);
            ReflectionUtil.SetPrivateField(newLevel, "_authorName", song.authorName);
            ReflectionUtil.SetPrivateField(newLevel, "_songName", song.songName);
            ReflectionUtil.SetPrivateField(newLevel, "_songSubName", song.songSubName);
            ReflectionUtil.SetPrivateField(newLevel, "_previewStartTime", song.previewStartTime);
            ReflectionUtil.SetPrivateField(newLevel, "_previewDuration", song.previewDuration);
            ReflectionUtil.SetPrivateField(newLevel, "_beatsPerMinute", song.beatsPerMinute);
            StartCoroutine(LoadSprite("file://" + song.path + "/" + song.coverImagePath, newLevel, "_coverImage"));

            SceneInfo newSceneInfo = ScriptableObject.CreateInstance<SceneInfo>();
            ReflectionUtil.SetPrivateField(newSceneInfo, "_gameScenesManager", gameScenesManager);
            ReflectionUtil.SetPrivateField(newSceneInfo, "_sceneName", song.environmentName);

            ReflectionUtil.SetPrivateField(newLevel, "_environmetSceneInfo", newSceneInfo);

            List<CustomLevelStaticData.CustomDifficultyLevel> difficultyLevels = new List<CustomLevelStaticData.CustomDifficultyLevel>();
            foreach (CustomSongInfo.DifficultyLevel diffLevel in song.difficultyLevels)
            {
                CustomLevelStaticData.CustomDifficultyLevel newDiffLevel = new CustomLevelStaticData.CustomDifficultyLevel();
                try
                {
                    LevelStaticData.Difficulty difficulty = diffLevel.difficulty.ToEnum(LevelStaticData.Difficulty.Normal);
                    ReflectionUtil.SetPrivateField(newDiffLevel, "_difficulty", difficulty);
                    ReflectionUtil.SetPrivateField(newDiffLevel, "_difficultyRank", diffLevel.difficultyRank);

                    if (!File.Exists(song.path + "/" + diffLevel.jsonPath))
                    {
                        Logger.Log("Couldn't find difficulty json " + song.path + "/" + diffLevel.jsonPath);
                        continue;
                    }

                    newDiffLevel.jsonPath = song.path + "/" + diffLevel.jsonPath;
                    newDiffLevel.audioPath = song.path + "/" + diffLevel.audioPath;
                    difficultyLevels.Add(newDiffLevel);
                }
                catch (Exception e)
                {
                    Logger.Log("Error parsing difficulty level in song: " + song.path);
                    Logger.Log(e.Message);
                    continue;
                }
            }

            if (difficultyLevels.Count == 0) return null;

            ReflectionUtil.SetPrivateField(newLevel, "_difficultyLevels", difficultyLevels.ToArray());
            return newLevel;
        }

        private SongLevelData ParseDifficulty(string jsonPath)
        {
            SongLevelData newSongLevelData = ScriptableObject.CreateInstance<SongLevelData>();
            string json = File.ReadAllText(jsonPath);
            try
            {
                newSongLevelData.LoadFromJson(json);
            }
            catch (Exception e)
            {
                Logger.Log("Error while parsing " + jsonPath);
                Logger.Log(e.ToString());
            }
            return newSongLevelData;
        }

        private void RemoveCustomScores()
        {
            if (PlayerPrefs.HasKey("lbPatched")) return;
            _leaderboardScoreUploader = FindObjectOfType<LeaderboardScoreUploader>();
            if (_leaderboardScoreUploader == null) return;
            List<LeaderboardScoreUploader.ScoreData> scores =
                ReflectionUtil.GetPrivateField<List<LeaderboardScoreUploader.ScoreData>>(_leaderboardScoreUploader,
                    "_scoresToUploadForCurrentPlayer");

            List<LeaderboardScoreUploader.ScoreData> scoresToRemove = new List<LeaderboardScoreUploader.ScoreData>();
            foreach (LeaderboardScoreUploader.ScoreData scoreData in scores)
            {
                string[] split = scoreData._leaderboardId.Split('_');
                string levelID = split[0];
                if (CustomSongInfos.Any(x => x.levelId == levelID))
                {
                    Logger.Log("Removing a custom score here");
                    scoresToRemove.Add(scoreData);
                }
            }

            scores.RemoveAll(x => scoresToRemove.Contains(x));
        }

        private IEnumerator LoadAudio(string audioPath, object obj, string fieldName)
        {
            using (WWW www = new WWW(audioPath))
            {
                //yield return www;
                while (!www.isDone)
                    www.MoveNext();
                ReflectionUtil.SetPrivateField(obj, fieldName, www.GetAudioClip(true, true, AudioType.UNKNOWN));
            }
            yield return null;
        }

        private IEnumerator LoadSprite(string spritePath, object obj, string fieldName)
        {
            Texture2D tex;
            tex = new Texture2D(256, 256, TextureFormat.DXT1, false);
            using (WWW www = new WWW(spritePath))
            {
                yield return www;
                www.LoadImageIntoTexture(tex);
                Sprite newSprite = Sprite.Create(tex, new Rect(0, 0, 256, 256), Vector2.one * 0.5f, 100, 1);
                ReflectionUtil.SetPrivateField(obj, fieldName, newSprite);
            }
        }

        private List<string> GetAllSongFolders()
        {
            List<string> songs = new List<string>();

            string path = Environment.CurrentDirectory;
            path = path.Replace('\\', '/');

            List<string> currentHashes = new List<string>();
            string[] cachedSongs = new string[0];
            if (Directory.Exists(path + "/CustomSongs/.cache"))
            {
                cachedSongs = Directory.GetDirectories(path + "/CustomSongs/.cache");
            }
            else
            {
                Directory.CreateDirectory(path + "/CustomSongs/.cache");
            }

            string[] songZips = Directory.GetFiles(path + "/CustomSongs")
                .Where(x => x.ToLower().EndsWith(".zip") || x.ToLower().EndsWith(".beat")).ToArray();
            foreach (string songZip in songZips)
            {
                Logger.Log("Found zip: " + songZip);
                //Check cache if zip already is extracted
                string hash;
                if (Utils.CreateMD5FromFile(songZip, out hash))
                {
                    currentHashes.Add(hash);
                    if (cachedSongs.Any(x => x.Contains(hash))) continue;

                    using (Unzip unzip = new Unzip(songZip))
                    {
                        unzip.ExtractToDirectory(path + "/CustomSongs/.cache/" + hash);
                        Logger.Log("Extracted to " + path + "/CustomSongs/.cache/" + hash);
                    }
                }
                else
                {
                    Logger.Log("Error reading zip " + songZip);
                }
            }

            List<string> songFolders = Directory.GetDirectories(path + "/CustomSongs").ToList();
            string[] songCaches = Directory.GetDirectories(path + "/CustomSongs/.cache");

            foreach (string song in songFolders)
            {
                string[] results = Directory.GetFiles(song, "info.json", SearchOption.AllDirectories);
                if (results.Length == 0)
                {
                    Logger.Log("Custom song folder '" + song + "' is missing info.json!");
                    continue;
                }

                foreach (string result in results)
                {
                    string songPath = Path.GetDirectoryName(result).Replace('\\', '/');
                    songs.Add(songPath);
                }
            }

            foreach (string song in songCaches)
            {
                string hash = Path.GetFileName(song);
                if (!currentHashes.Contains(hash))
                {
                    //Old cache
                    Logger.Log("Deleting old cache: " + song);
                    Directory.Delete(song, true);
                }
            }
            return songs;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                if (Input.GetKey(KeyCode.LeftControl))
                    _database.UpdateSongDB(GetAllSongFolders().ToArray(), Input.GetKey(KeyCode.LeftShift));
                RefreshSongs(true);
            }
        }
    }
}