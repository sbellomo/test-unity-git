using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using GitHub.Unity;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace GitHub.Unity
{


	public class ShowPopup : EditorWindow
	{
		string message = "";
		public static void Init(string message)
		{
			ShowPopup window = ScriptableObject.CreateInstance<ShowPopup>();
			window.position = new Rect(Screen.width / 2, Screen.height / 2, 250, 150);
			window.Show();
			//window.ShowNotification(new GUIContent(message));
			//window.ShowPopup();
			window.message = message;
		}

		void OnGUI()
		{
			EditorGUILayout.LabelField(this.message, EditorStyles.wordWrappedLabel);
			GUILayout.Space(70);
			if (GUILayout.Button("Ok")) this.Close();
		}
	}

	[InitializeOnLoad]
	public class SceneValidationReadOnly
	{
		static SceneValidationReadOnly()
		{
			//EditorSceneManager.sceneLoaded += OnSceneLoaded;
			EditorSceneManager.sceneOpening += OnSceneLoading;
			//SceneManager.sceneLoaded += OnSceneLoaded;

		}
		static void OnSceneLoading(string path, OpenSceneMode mode)
		{
			Debug.Log("OnSceneLoading " + path + " " + mode);
			if (!FileModifiedChecker.CanEditFile(path))
			{
				string message = "ERROR can't edit file " + path + " since it's currently not editable and might be locked. Further edits might be lost";
				Debug.LogError(message);
				ShowPopup.Init(message);
			}
		}
		static void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
		{
			Debug.Log("OnSceneLoaded " + scene + " " + loadSceneMode);
		}
	}
	public class FileModifiedChecker : UnityEditor.AssetModificationProcessor
	{

		public static bool CanEditFile(string path)
		{
			Debug.Log("checking if can edit file " + path);
			bool isReadOnly = !new FileInfo(path).IsReadOnly;
			if (isReadOnly && !String.IsNullOrEmpty(path))
			{
				string gitRepoPath = "C:\\Users\\sbellomo\\Documents\\GitHub\\test-from-scratch\\New Unity Project 1"; //TODO get this from settings

				var relativePath = path;
				//var relativePathArray = path.Split(new string[] { gitRepoPath }, StringSplitOptions.None);
				//var relativePath = relativePathArray[0];
				var task = EntryPoint.ApplicationManager.Environment.Repository.RequestLock(relativePath); 
				task.Start();
				task.Wait(200);
			}
			return !new FileInfo(path).IsReadOnly;
		}
		public static bool IsOpenForEdit(string path, ref string message)
		{
			//Debug.Log("checking is open for edit " + path);

			if (!CanEditFile(path))
			{
				message = "nooo can't touch this...";
				return false;
			}
			return true;
		}

		public static string[] OnWillSaveAssets(string[] paths)
		{
			List<string> tmp = new List<string>();
			foreach (var path in paths)
			{
				if (CanEditFile(path))
				{
					tmp.Add(path);
				}
				else
				{
					string message = "file " + path + "can't be saved since it's read only, please unlock it to edit it";
					Debug.LogError(message);
					ShowPopup.Init(message);

				}
			}
			return tmp.ToArray();
		}

	}
}