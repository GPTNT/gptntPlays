using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

public class AtomicObservationRequest
{
	public long? AnchorFrameSequence;
	public long? Epoch;
	public long? AudioCursor;
}

public class AtomicObservationSnapshot
{
	public TimedRawObservationPayload Frames;
	public byte[] Segmentation;
	public long SegmentationFrameSequence;
	public short[] AudioSamples;
	public int AudioSampleRate;
	public long? RequestedAudioCursor;
	public long AudioStartCursor;
	public long AudioEndCursor;
	public long AudioDroppedSamples;
	public long? RequestedEpoch;
	public long? RequestedAnchorFrameSequence;
}

public class AtomicObservationHeader
{
	public int version;
	public bool containsBombState;
	public long epoch;
	public int frameWidth;
	public int frameHeight;
	public string pixelFormat;
	public int frameByteLength;
	public int frameCount;
	public List<FrameTimingPayload> frames;
	public long? requestedEpoch;
	public long? requestedAnchorFrameSequence;
	public bool anchorRequested;
	public bool anchorIncluded;
	public bool frameCoverageGap;
	public long oldestAvailableFrameSequence;
	public long endFrameSequence;
	public bool segmentationIncluded;
	public int segmentationByteLength;
	public long segmentationFrameSequence;
	public string audioEncoding;
	public int audioSampleRate;
	public int audioChannels;
	public int audioSampleCount;
	public int audioByteLength;
	public long? requestedAudioCursor;
	public long audioStartCursor;
	public long audioEndCursor;
	public long audioDroppedSamples;
	public bool audioCoverageGap;
	public bool coverageGap;
}

public static class AtomicObservationWriter
{
	private static readonly byte[] Magic = Encoding.ASCII.GetBytes("GPTNTOB1");

	public static void Write(HttpListenerResponse response, AtomicObservationSnapshot snapshot)
	{
		TimedRawObservationPayload frames = snapshot.Frames;
		int frameByteLength = frames.frameWidth * frames.frameHeight * 3;
		int segmentationLength = snapshot.Segmentation == null ? 0 : snapshot.Segmentation.Length;
		int audioByteLength = snapshot.AudioSamples.Length * sizeof(short);
		bool audioCoverageGap = snapshot.RequestedAudioCursor.HasValue
			&& snapshot.AudioStartCursor != snapshot.RequestedAudioCursor.Value;

		AtomicObservationHeader header = new AtomicObservationHeader
		{
			version = 1,
			containsBombState = false,
			epoch = frames.epoch,
			frameWidth = frames.frameWidth,
			frameHeight = frames.frameHeight,
			pixelFormat = "rgb24",
			frameByteLength = frameByteLength,
			frameCount = frames.rawFrames.Count,
			frames = frames.frameTiming,
			requestedEpoch = snapshot.RequestedEpoch,
			requestedAnchorFrameSequence = snapshot.RequestedAnchorFrameSequence,
			anchorRequested = frames.anchorRequested,
			anchorIncluded = frames.anchorIncluded,
			frameCoverageGap = frames.coverageGap,
			oldestAvailableFrameSequence = frames.oldestAvailableSequence,
			endFrameSequence = frames.endFrameSequence,
			segmentationIncluded = snapshot.Segmentation != null,
			segmentationByteLength = segmentationLength,
			segmentationFrameSequence = snapshot.SegmentationFrameSequence,
			audioEncoding = "pcm_s16le",
			audioSampleRate = snapshot.AudioSampleRate,
			audioChannels = 1,
			audioSampleCount = snapshot.AudioSamples.Length,
			audioByteLength = audioByteLength,
			requestedAudioCursor = snapshot.RequestedAudioCursor,
			audioStartCursor = snapshot.AudioStartCursor,
			audioEndCursor = snapshot.AudioEndCursor,
			audioDroppedSamples = snapshot.AudioDroppedSamples,
			audioCoverageGap = audioCoverageGap,
			coverageGap = frames.coverageGap || audioCoverageGap,
		};

		byte[] headerBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(header));
		long contentLength = Magic.Length + sizeof(int) + headerBytes.Length
			+ ((long) frameByteLength * frames.rawFrames.Count)
			+ segmentationLength
			+ audioByteLength;

		response.StatusCode = (int) HttpStatusCode.OK;
		response.ContentType = "application/vnd.gptnt.observation";
		response.ContentLength64 = contentLength;

		using (BinaryWriter writer = new BinaryWriter(response.OutputStream, Encoding.UTF8))
		{
			writer.Write(Magic);
			writer.Write(headerBytes.Length);
			writer.Write(headerBytes);
			foreach (byte[] frame in frames.rawFrames)
				writer.Write(frame);
			if (snapshot.Segmentation != null)
				writer.Write(snapshot.Segmentation);
			foreach (short sample in snapshot.AudioSamples)
				writer.Write(sample);
			writer.Flush();
		}
	}
}
