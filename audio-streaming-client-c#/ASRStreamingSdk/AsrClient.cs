﻿using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;
using System.Threading.Tasks;
using static Com.Baidu.Acu.Pie.AsrService;
using Google.Protobuf;
using System.Text;

namespace Com.Baidu.Acu.Pie
{
    public class AsrStream
    {
        private uint sequence = 0;
        private readonly AsyncDuplexStreamingCall<AudioFragmentRequest, AudioFragmentResponse> stream;
        internal AsrStream(AsyncDuplexStreamingCall<AudioFragmentRequest, AudioFragmentResponse> stream)
        {
            this.stream = stream;
        }

        public Task Write(byte[] audioData)
        {
            return Write(audioData, 0, audioData.Length);
        }

        public Task Write(byte[] audioData, int offset, int count)
        {
            AudioFragmentRequest request = new AudioFragmentRequest();
            request.SequenceNum = sequence++;
            request.SendTimestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            request.AudioData = ByteString.CopyFrom(audioData, offset, count);
            return stream.RequestStream.WriteAsync(request);
        }

        public Task WriteComplete()
        {
            return stream.RequestStream.CompleteAsync();
        }

        public Task<bool> MoveNext()
        {
            return stream.ResponseStream.MoveNext();
        }

        public AudioFragmentResponse Current()
        {
            return stream.ResponseStream.Current;
        }
    }

    public class StreamToken
    {
        public string UserName { get; }
        public DateTime Expire { get; }
        public string Password { get; }
        public string ExpireString
        {
            get
            {
                return Expire.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'");
            }
        }
        public string Token
        {
            get
            {
                var crypt = new SHA256Managed();
                var bytes = crypt.ComputeHash(Encoding.ASCII.GetBytes(UserName + Password + ExpireString));
                StringBuilder result = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    result.Append(bytes[i].ToString("X2"));
                }
                return result.ToString().ToLower();
            }
        }

        public StreamToken(string userName, DateTime expire, string password)
        {
            this.UserName = userName;
            this.Expire = expire;
            this.Password = password;
        }
    }

    public class AsrClient
    {
        private Channel channel;
        public bool Flush { get; set; }
        public string ProductId { get; }
        public string AppName { get; set; }
        public double SendPerSeconds { get; set; }
        public double SleepRatio { get; set; }
        public uint LogLevel { get; set; }
        public int RecommendPacketSize
        {
            get
            {
                return (int)(SendPerSeconds * bitrate * 2);
            }
        }

        private readonly int bitrate;
        private AsrServiceClient client;
        public AsrClient(string serverAddress, string productId)
        {
            switch (productId)
            {
                case "1903":
                case "1904":
                case "1905":
                case "1906":
                case "1907":
                    bitrate = 8000;
                    break;
                case "888":
                case "1888":
                    bitrate = 16000;
                    break;
                default:
                    throw new Exception("Unrecoginzed product id");
            }
            this.ProductId = productId;
            this.SleepRatio = 1;
            this.SendPerSeconds = 0.02;
            this.LogLevel = 4;
            this.AppName = "csharp_sdk";
            this.Flush = true;
            channel = new Channel(serverAddress, ChannelCredentials.Insecure);
            client = new AsrServiceClient(channel);
        }

        ~AsrClient()
        {
            // do not wait
            channel.ShutdownAsync();
        }

        public AsrStream NewStream()
        {
            return NewStream(null);
        }

        public AsrStream NewStream(StreamToken streamToken)
        {

            InitRequest initRequest = new InitRequest
            {
                ProductId = this.ProductId,
                AppName = this.AppName,
                EnableChunk = true,
                EnableFlushData = this.Flush,
                EnableLongSpeech = true,
                LogLevel = this.LogLevel,
                SleepRatio = this.SleepRatio,
                SendPerSeconds = this.SendPerSeconds,
                SamplePointBytes = 2,
                Version = ProtoVersion.Version1
            };
            if (streamToken != null)
            {
                initRequest.UserName = streamToken.UserName;
                initRequest.ExpireTime = streamToken.ExpireString;
                initRequest.Token = streamToken.Token;
            }
            Console.WriteLine("initRequest:{0}", initRequest.ToString());
            Metadata meta = new Metadata
            {
                { "audio_meta", initRequest.ToByteString().ToBase64() }
            };
            var stream = client.send(meta);
            return new AsrStream(stream);
        }
    }
}
