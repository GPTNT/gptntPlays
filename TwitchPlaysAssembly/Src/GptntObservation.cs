using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

public class AtomicObservationRequest
{
	// The anchor is the final video frame returned previously. Repeating it at the
	// start of the next video lets the model compare before and after an action.
	public long? AnchorFrameSequence;

	// An epoch is one mission/reset generation. A cursor from another epoch cannot
	// establish continuous coverage even if its numeric value looks plausible.
	public long? Epoch;

	// Absolute mono-sample cursor returned by the preceding observation.
	public long? AudioCursor;
}

// A main-thread capture result. None of its arrays are modified after creation,
// allowing the HTTP worker to serialize it without touching Unity APIs or locks.
public class AtomicObservationSnapshot
{
	public TimedRawObservationPayload Frames;
	public byte[] CurrentImage;
	public FrameTimingPayload CurrentImageTiming;
	public byte[] Segmentation;
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
	// Version covers both this JSON schema and the order of binary body sections.
	public int version;

	// This is deliberately explicit: visual observations must not silently acquire
	// privileged module state as the endpoint evolves.
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
	public int currentImageByteLength;
	public FrameTimingPayload currentImageTiming;
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
	// Eight-byte magic identifies the payload before a client trusts any lengths.
	// The final digit is the wire-format version for quick inspection and recovery.
	private static readonly byte[] Magic = Encoding.ASCII.GetBytes("GPTNTOB1");

	public static void Write(HttpListenerResponse response, AtomicObservationSnapshot snapshot)
	{
		TimedRawObservationPayload frames = snapshot.Frames;
		int frameByteLength = frames.frameWidth * frames.frameHeight * 3;
		int currentImageLength = snapshot.CurrentImage.Length;
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
			currentImageByteLength = currentImageLength,
			currentImageTiming = snapshot.CurrentImageTiming,
			segmentationIncluded = snapshot.Segmentation != null,
			segmentationByteLength = segmentationLength,
			segmentationFrameSequence = snapshot.CurrentImageTiming.sequence,
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

		// Precompute the exact length because HttpListener sends a fixed-length local
		// response. Raw media follows the small JSON header without base64 expansion.
		long contentLength = Magic.Length + sizeof(int) + headerBytes.Length
			+ ((long) frameByteLength * frames.rawFrames.Count)
			+ currentImageLength
			+ segmentationLength
			+ audioByteLength;

		response.StatusCode = (int) HttpStatusCode.OK;
		response.ContentType = "application/vnd.gptnt.observation";
		response.ContentLength64 = contentLength;

		using (BinaryWriter writer = new BinaryWriter(response.OutputStream, Encoding.UTF8))
		{
			// Wire order: magic, JSON header, video frames, current RGB image,
			// segmentation, then PCM audio.
			// The header carries every count needed to split the untagged binary sections.
			writer.Write(Magic);
			writer.Write(headerBytes.Length);
			writer.Write(headerBytes);
			foreach (byte[] frame in frames.rawFrames)
				writer.Write(frame);
			writer.Write(snapshot.CurrentImage);
			if (snapshot.Segmentation != null)
				writer.Write(snapshot.Segmentation);
			foreach (short sample in snapshot.AudioSamples)
				writer.Write(sample);
			writer.Flush();
		}
	}
}
