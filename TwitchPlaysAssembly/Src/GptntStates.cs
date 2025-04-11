using System.IO;
using System;
using UnityEngine;
using System.Collections;

public class GptntStates : MonoBehaviour 
{
	public string logFilePath;
	public long lastPosition = 0;
	private string line;
	public string serialNumber;

	public void Awake()
	{
	}

	public string getSerialNumber()
	{
		try
		{
			if (File.Exists(logFilePath))
			{
				using (FileStream fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					fs.Seek(lastPosition, SeekOrigin.Begin); // jump to where we left off

					using (StreamReader reader = new StreamReader(fs))
					{
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Contains("Randomizing Serial Number"))
							{
								string[] newContentArray = line.Split(':');
								GptntDebug.Log($"new line: {line}\n\n");
								serialNumber = newContentArray[3];
							}
						}
					}
					return serialNumber;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"Error while copying log: {ex.Message}");
		}
		return null;
	}

	public IEnumerator populate(float delay)
	{
		yield return new WaitForSeconds(delay);

		serialNumber = getSerialNumber();
	}

}
