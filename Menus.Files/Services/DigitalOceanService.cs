using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Configuration;

namespace Menus.Files.Services
{
    // S3-сумісне сховище медіа (DigitalOcean Spaces, Cloudflare R2 або Amazon S3) - налаштування в секції Media:
    //   ServiceUrl    - endpoint S3-сумісного сховища; порожній = Amazon S3 у регіоні Region
    //   Region, Bucket, AccessKey, SecretKey
    //   PublicReadAcl - false для бакета за CloudFront (ACL вимкнені)
    public class DigitalOceanService
    {
        private readonly AmazonS3Client _client;
        private readonly string _bucket;
        private readonly bool _publicRead;

        public DigitalOceanService(IConfiguration config)
        {
            var serviceUrl = config["Media:ServiceUrl"];
            var region = config["Media:Region"];
            var s3Config = string.IsNullOrEmpty(serviceUrl)
                ? new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(region) }
                : new AmazonS3Config { ServiceURL = serviceUrl };
            // S3-сумісні сховища (Cloudflare R2 - регіон "auto") вимагають явного регіону в підписі запиту
            if (!string.IsNullOrEmpty(serviceUrl) && !string.IsNullOrEmpty(region))
                s3Config.AuthenticationRegion = region;

            _client = new AmazonS3Client(config["Media:AccessKey"], config["Media:SecretKey"], s3Config);
            _bucket = config["Media:Bucket"];
            _publicRead = !string.Equals(config["Media:PublicReadAcl"], "false", StringComparison.OrdinalIgnoreCase);
        }

        ~DigitalOceanService()
        {
            _client.Dispose();
        }

        public async Task<bool> Exists(string name)
        {
            try
            {
                await _client.GetObjectMetadataAsync(_bucket, name);
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task<string> ReadText(string name)
        {
            using (var response = await _client.GetObjectAsync(_bucket, name))
            using (var reader = new StreamReader(response.ResponseStream))
            {
                return await reader.ReadToEndAsync();
            }
        }

        public async Task UploadFile(byte[] file, string name)
        {
            using (var memoryStream = new MemoryStream(file))
            {
                await _client.PutObjectAsync(CreateRequest(memoryStream, name));
            }
        }

        public async Task UploadFile(string filePath, string name)
        {
            using (var fileStream = File.OpenRead(filePath))
            {
                await _client.PutObjectAsync(CreateRequest(fileStream, name));
            }
        }

        // Один PutObject без chunked-підпису: Cloudflare R2 не підтримує STREAMING-AWS4-HMAC-SHA256-PAYLOAD
        // (файли невеликі - фото й html, multipart з TransferUtility не потрібен)
        private PutObjectRequest CreateRequest(Stream stream, string name)
        {
            var request = new PutObjectRequest
            {
                InputStream = stream,
                Key = name,
                BucketName = _bucket,
                ContentType = AmazonS3Util.MimeTypeFromExtension(Path.GetExtension(name)),
                UseChunkEncoding = false
            };
            if (_publicRead) request.CannedACL = S3CannedACL.PublicRead;
            // фото (files/) мають унікальні імена і не змінюються - браузер і Cloudflare тримають їх рік;
            // iframes/*.html перезаписуються під тим самим іменем, тому без довгого кешу
            if (name.StartsWith("files/")) request.Headers.CacheControl = "public, max-age=31536000, immutable";
            return request;
        }
    }
}
