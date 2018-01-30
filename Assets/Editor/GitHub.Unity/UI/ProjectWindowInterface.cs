using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace GitHub.Unity
{
    class ProjectWindowInterface : AssetPostprocessor
	{
		private static readonly List<GitStatusEntry> entries = new List<GitStatusEntry>();
		private static List<GitLock> _locks = new List<GitLock>();
		private static List<GitLock> locks
		{
			get
			{
				var lockCache = EntryPoint.ApplicationManager.Environment.CacheContainer.GitLocksCache;
				lockCache.ValidateData();
				_locks = lockCache.GitLocks;
				return _locks;
			}
			set
			{
				_locks = value;
			}
		}

		private static readonly List<string> guids = new List<string>();
        private static readonly List<string> guidsLocks = new List<string>();
        private static IRepository _repository;
		private static IRepository repository
		{
			get
			{
				if (_repository == null)
				{
					_repository = EntryPoint.ApplicationManager.Environment.Repository;
				}
				return _repository;
			}
			set
			{
				_repository = value;
			}
		}
		private static DateTime busyTimeStamp;
		private static bool _isBusy = false;
		private static bool isBusy
		{
			get
			{
				var now = DateTime.Now;
				var busyTimeout = 5;
				if (_isBusy && busyTimeStamp.AddSeconds(busyTimeout) < now)
				{
					Debug.LogWarning("Warning, has been busy for too long: longer than "+busyTimeout+", there might be an error somewhere. Unsetting the busy flag");
					_isBusy = false;
				}
				return _isBusy;
			}
			set
			{
				var now = DateTime.Now;
				busyTimeStamp = now;
				_isBusy = value;
			}
		}
        private static ILogging logger;
        private static ILogging Logger { get { return logger = logger ?? Logging.GetLogger<ProjectWindowInterface>(); } }
        private static CacheUpdateEvent lastRepositoryStatusChangedEvent;
        private static CacheUpdateEvent lastLocksChangedEvent;

        public static void Initialize(IRepository repo)
        {
            Logger.Trace("Initialize HasRepository:{0}", repo != null);

            EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;

            repository = repo;

            if (repository != null)
            {
                repository.TrackingStatusChanged += RepositoryOnStatusChanged;
                repository.LocksChanged += RepositoryOnLocksChanged;

                repository.CheckStatusChangedEvent(lastRepositoryStatusChangedEvent);
                repository.CheckLocksChangedEvent(lastLocksChangedEvent);
            }
        }

        private static void RepositoryOnStatusChanged(CacheUpdateEvent cacheUpdateEvent)
        {
            if (!lastRepositoryStatusChangedEvent.Equals(cacheUpdateEvent))
            {
                lastRepositoryStatusChangedEvent = cacheUpdateEvent;
                entries.Clear();
                entries.AddRange(repository.CurrentChanges);
                OnStatusUpdate();
            }
        }

        private static void RepositoryOnLocksChanged(CacheUpdateEvent cacheUpdateEvent)
        {
            if (!lastLocksChangedEvent.Equals(cacheUpdateEvent))
            {
                lastLocksChangedEvent = cacheUpdateEvent;
                locks = repository.CurrentLocks;
                OnLocksUpdate();
            }
        }

        [MenuItem("Assets/Request Lock", true)]
        private static bool ContextMenu_CanLock()
        {
			if (isBusy)
			{
				Logger.Trace("is busy can't lock");
				return false;
			}
			if (repository == null || !repository.CurrentRemote.HasValue)
			{
				Logger.Trace("can't lock, repository is null or there's not remote");
				return false;
			}
            var selected = Selection.activeObject;
			if (selected == null)
			{
				Logger.Trace("can't lock, selected is null");
				return false;
			}

			if (locks == null)
			{
				Logger.Trace("can't lock, locks is null");
				return false;
			}
            NPath assetPath = AssetDatabase.GetAssetPath(selected.GetInstanceID()).ToNPath();
            NPath repositoryPath = EntryPoint.Environment.GetRepositoryPath(assetPath);

            var alreadyLocked = locks.Any(x =>
            {
                return repositoryPath == x.Path.ToNPath();

            });
            GitFileStatus status = GitFileStatus.None;
            if (entries != null)
            {
                status = entries.FirstOrDefault(x => repositoryPath == x.Path.ToNPath()).Status;
            }
			Logger.Trace("checking if can lock, already locked? {0}, status {1}", alreadyLocked, status);
            return !alreadyLocked && status != GitFileStatus.Untracked && status != GitFileStatus.Ignored;
        }

		[MenuItem("Assets/Request Lock")]
		private static void ContextMenu_Lock()
		{
			isBusy = true;
			var selected = Selection.activeObject;

			NPath assetPath = AssetDatabase.GetAssetPath(selected.GetInstanceID()).ToNPath();
			NPath repositoryPath = EntryPoint.Environment.GetRepositoryPath(assetPath);
			try
			{
				Logger.Trace("requesting lock on repository {0}", repositoryPath);
				repository
					.RequestLock(repositoryPath)
					.ThenInUI(_ =>
					{
						isBusy = false;
						Selection.activeGameObject = null;
						EditorApplication.RepaintProjectWindow();
					})
					.Start();
			}
			catch (Exception e)
			{
				Logger.Error(e);
			}
		}

        [MenuItem("Assets/Release lock", true, 1000)]
        private static bool ContextMenu_CanUnlock()
        {
			if (isBusy)
			{
				Logger.Trace("can't unlock, is busy");
				return false;
			}
			if (repository == null || !repository.CurrentRemote.HasValue)
			{
				Logger.Trace("can't unlock, repository is null or remote has no value");
				return false;
			}
			var selected = Selection.activeObject;
			if (selected == null)
			{
				Logger.Trace("can't unlock, selection is null");
				return false;
			}
			if (locks == null || locks.Count == 0)
			{
				Logger.Trace("can't unlock, lock is null or locks count is zero");
				return false;
			}
            NPath assetPath = AssetDatabase.GetAssetPath(selected.GetInstanceID()).ToNPath();
            NPath repositoryPath = EntryPoint.Environment.GetRepositoryPath(assetPath);

            var isLocked = locks.Any(x => repositoryPath == x.Path.ToNPath());

			Logger.Trace("is locked? {0} locks: {1}, repositoryPath {2}", isLocked, locks, repositoryPath);

			return isLocked;
        }

        [MenuItem("Assets/Release lock", false, 1000)]
        private static void ContextMenu_Unlock()
        {
            isBusy = true;
            var selected = Selection.activeObject;

			string assetPathString = AssetDatabase.GetAssetPath(selected.GetInstanceID());

			NPath assetPath = assetPathString.ToNPath();
            NPath repositoryPath = EntryPoint.Environment.GetRepositoryPath(assetPath);

            repository
                .ReleaseLock(repositoryPath, false)
                .ThenInUI(_ =>
                {
                    isBusy = false;
                    Selection.activeGameObject = null;
                    EditorApplication.RepaintProjectWindow();
                })
                .Start();

			NPath metaAssetPath = (assetPathString + ".meta").ToNPath();
			NPath metaRepositoryPath = EntryPoint.Environment.GetRepositoryPath(metaAssetPath);
			repository
				.ReleaseLock(metaRepositoryPath, false)
				.ThenInUI(_ =>
				{
					isBusy = false;
					Selection.activeGameObject = null;
					EditorApplication.RepaintProjectWindow();
				})
				.Start();
		}

        private static void OnLocksUpdate()
        {
            if (locks == null)
            {
                return;
            }
            locks = locks.ToList();

            guidsLocks.Clear();
            foreach (var lck in locks)
            {
                NPath repositoryPath = lck.Path.ToNPath();
                NPath assetPath = EntryPoint.Environment.GetAssetPath(repositoryPath);

                var g = AssetDatabase.AssetPathToGUID(assetPath);
                guidsLocks.Add(g);
            }

            EditorApplication.RepaintProjectWindow();
        }

        private static void OnStatusUpdate()
        {
            guids.Clear();
            for (var index = 0; index < entries.Count; ++index)
            {
                var gitStatusEntry = entries[index];

                var path = gitStatusEntry.ProjectPath;
                if (gitStatusEntry.Status == GitFileStatus.Ignored)
                {
                    continue;
                }

                if (!path.StartsWith("Assets", StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                if (path.EndsWith(".meta", StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                var guid = AssetDatabase.AssetPathToGUID(path);
                guids.Add(guid);
            }

            EditorApplication.RepaintProjectWindow();
        }

        private static void OnProjectWindowItemGUI(string guid, Rect itemRect)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(guid))
            {
                return;
            }

            var index = guids.IndexOf(guid);
            var indexLock = guidsLocks.IndexOf(guid);

            if (index < 0 && indexLock < 0)
            {
                return;
            }

            GitStatusEntry? gitStatusEntry = null;
            GitFileStatus status = GitFileStatus.None;

            if (index >= 0)
            {
                gitStatusEntry = entries[index];
                status = gitStatusEntry.Value.Status;
            }

            var isLocked = indexLock >= 0;
            var texture = Styles.GetFileStatusIcon(status, isLocked);

            if (texture == null)
            {
                var path = gitStatusEntry.HasValue ? gitStatusEntry.Value.Path : string.Empty;
                Logger.Warning("Unable to retrieve texture for Guid:{0} EntryPath:{1} Status: {2} IsLocked:{3}", guid, path, status.ToString(), isLocked);
                return;
            }

            Rect rect;

            // End of row placement
            if (itemRect.width > itemRect.height)
            {
                rect = new Rect(itemRect.xMax - texture.width, itemRect.y, texture.width,
                    Mathf.Min(texture.height, EditorGUIUtility.singleLineHeight));
            }
            // Corner placement
            // TODO: Magic numbers that need reviewing. Make sure this works properly with long filenames and wordwrap.
            else
            {
                var scale = itemRect.height / 90f;
                var size = new Vector2(texture.width * scale, texture.height * scale);
                var offset = new Vector2(itemRect.width * Mathf.Min(.4f * scale, .2f), itemRect.height * Mathf.Min(.2f * scale, .2f));
                rect = new Rect(itemRect.center.x - size.x * .5f + offset.x, itemRect.center.y - size.y * .5f + offset.y, size.x, size.y);
            }

            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit);
        }
    }
}
