using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

public class FeedbackObservationSnapshot
{
	public RawObservationPayload Frames;
	public List<FeedbackFrame> FrameFeedback;
	public long Epoch;
	public byte[] CurrentImage;
	public FeedbackFrame CurrentImageFeedback;
	public byte[] Segmentation;
}

public class FeedbackObservationHeader
{
	public int version = 1;
	public int feedbackVersion = 1;
	public bool containsBombState = false;
	public long epoch;
	public int frameWidth;
	public int frameHeight;
	public int frameByteLength;
	public int frameCount;
	public List<FeedbackFrame> frameFeedback;
	public FeedbackFrame currentImageFeedback;
	public int currentImageByteLength;
	public bool segmentationIncluded;
	public int segmentationByteLength;
	public long segmentationFrameSequence;
}

public static class FeedbackObservationWriter
{
	public static void Write(HttpListenerResponse response, FeedbackObservationSnapshot snapshot)
	{
		RawObservationPayload frames = snapshot.Frames;
		FeedbackObservationHeader header = new FeedbackObservationHeader
		{
			feedbackVersion = snapshot.CurrentImageFeedback.version,
			epoch = snapshot.Epoch,
			frameWidth = frames.frameWidth, frameHeight = frames.frameHeight,
			frameByteLength = frames.frameWidth * frames.frameHeight * 3,
			frameCount = frames.rawFrames.Count, frameFeedback = snapshot.FrameFeedback,
			currentImageFeedback = snapshot.CurrentImageFeedback,
			currentImageByteLength = snapshot.CurrentImage.Length,
			segmentationIncluded = snapshot.Segmentation != null,
			segmentationByteLength = snapshot.Segmentation == null ? 0 : snapshot.Segmentation.Length,
			segmentationFrameSequence = snapshot.CurrentImageFeedback.sequence,
		};
		byte[] magic = Encoding.ASCII.GetBytes("GPTNTFB1");
		byte[] json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(header));
		response.ContentType = "application/octet-stream";
		response.StatusCode = (int) HttpStatusCode.OK;
		response.ContentLength64 = magic.Length + sizeof(int) + json.Length
			+ ((long) header.frameByteLength * header.frameCount)
			+ header.currentImageByteLength + header.segmentationByteLength;
		// BinaryWriter uses little-endian integers. The caller owns OutputStream.
		BinaryWriter writer = new BinaryWriter(response.OutputStream);
		writer.Write(magic);
		writer.Write(json.Length);
		writer.Write(json);
		foreach (byte[] frame in frames.rawFrames) writer.Write(frame);
		writer.Write(snapshot.CurrentImage);
		if (snapshot.Segmentation != null) writer.Write(snapshot.Segmentation);
		writer.Flush();
	}
}
