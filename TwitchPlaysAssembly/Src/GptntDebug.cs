using System;
using System.IO;
using UnityEngine;

public class GptntDebug : MonoBehaviour
{
	private static string path = Path.Combine(Application.persistentDataPath, "gptntlogs.log");

	public static void Log(string message)
	{
		StreamWriter writer = new StreamWriter(path, true);
		writer.WriteLine(message);
		writer.Close();
	}

}

