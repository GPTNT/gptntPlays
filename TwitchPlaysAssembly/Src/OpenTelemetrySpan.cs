using System;
using System.Collections.Generic;
using System.Net;
using Newtonsoft.Json;
using log4net;

public class OpenTelemetrySpan
{
	private static ILog log = LogManager.GetLogger("OTel");

	// Your OTEL collector endpoint
	private const string OTEL_ENDPOINT = "http://localhost:4318/v1/traces";

	private string traceId;
	private string spanId;
	private string parentSpanId;
	private string name;
	private long startTimeUnixNano;
	private Dictionary<string, object> attributes;

	public OpenTelemetrySpan(string name, string traceId = null, string parentSpanId = null)
	{
		this.name = name;
		this.traceId = traceId ?? GenerateTraceId();
		spanId = GenerateSpanId();
		this.parentSpanId = parentSpanId;
		startTimeUnixNano = DateTimeOffset.UtcNow.Millisecond * 1000000;
		attributes = new Dictionary<string, object>();

		//log.Debug($"Started span '{name}' trace_id={this.traceId} span_id={spanId}");
	}

	public void SetAttribute(string key, object value)
	{
		attributes[key] = value;
	}

	public void AddEvent(string name, Dictionary<string, object> eventAttributes = null)
	{
		// Store events if needed
		log.Debug(GptntDebug.FormatMessage($"Span event: {name}"));
	}

	public void End(bool success = true)
	{
		long endTimeUnixNano = DateTimeOffset.UtcNow.Millisecond * 1000000;

		// Set status
		attributes["otel.status_code"] = success ? "OK" : "ERROR";

		// Build OTLP JSON payload
		var payload = new
		{
			resourceSpans = new[]
			{
				new
				{
					resource = new
					{
						attributes = new[]
						{
							new { key = "service.name", value = new { stringValue = "unity-ktane" } },
							new { key = "game.version", value = new { stringValue = "1.0.0" } }
						}
					},
					scopeSpans = new[]
					{
						new
						{
							scope = new
							{
								name = "unity-manual-instrumentation",
								version = "1.0"
							},
							spans = new[]
							{
								new
								{
									traceId = traceId,
									spanId = spanId,
									parentSpanId = parentSpanId,
									name = name,
									kind = 1, // SPAN_KIND_INTERNAL
                                    startTimeUnixNano = startTimeUnixNano.ToString(),
									endTimeUnixNano = endTimeUnixNano.ToString(),
									attributes = ConvertAttributes(),
									status = new
									{
										code = success ? 1 : 2 // STATUS_CODE_OK or ERROR
                                    }
								}
							}
						}
					}
				}
			}
		};

		try
		{
			//var json = JsonConvert.SerializeObject(payload);

			//using (var client = new WebClient())
			//{
			//	client.Headers[HttpRequestHeader.ContentType] = "application/json";
			//	string response = client.UploadString(OTEL_ENDPOINT, json);
			//}
		}
		catch (Exception ex)
		{
			log.Error(GptntDebug.FormatMessage($"Error sending span"), ex);
		}

		//log.Debug(GptntDebug.FormatMessage($"Ended span '{name}' duration={(endTimeUnixNano - startTimeUnixNano) / 1000000}ms"));
	}

	private object[] ConvertAttributes()
	{
		var attrs = new List<object>();
		foreach (var kvp in attributes)
		{
			attrs.Add(new
			{
				key = kvp.Key,
				value = GetAttributeValue(kvp.Value)
			});
		}
		return attrs.ToArray();
	}

	private object GetAttributeValue(object value)
	{
		if (value is string s)
			return new { stringValue = s };
		if (value is int i)
			return new { intValue = i };
		if (value is bool b)
			return new { boolValue = b };
		if (value is double d)
			return new { doubleValue = d };

		return new { stringValue = value.ToString() };
	}

	public string GetTraceId() => traceId;
	public string GetSpanId() => spanId;

	private static string GenerateTraceId()
	{
		byte[] bytes = new byte[16];
		System.Security.Cryptography.RNGCryptoServiceProvider.Create().GetBytes(bytes);
		return BitConverter.ToString(bytes).Replace("-", "").ToLower();
	}

	private static string GenerateSpanId()
	{
		byte[] bytes = new byte[8];
		System.Security.Cryptography.RNGCryptoServiceProvider.Create().GetBytes(bytes);
		return BitConverter.ToString(bytes).Replace("-", "").ToLower();
	}

}
