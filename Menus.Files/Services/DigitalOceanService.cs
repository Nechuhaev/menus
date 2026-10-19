using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
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
        private readonly TransferUtility _do;
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
            _do = new TransferUtility(_client);
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
                await _do.UploadAsync(CreateRequest(memoryStream, name));
            }
        }

        public async Task UploadFile(string filePath, string name)
        {
            using (var fileStream = File.OpenRead(filePath))
            {
                await _do.UploadAsync(CreateRequest(fileStream, name));
            }
        }

        private TransferUtilityUploadRequest CreateRequest(Stream stream, string name)
        {
            var request = new TransferUtilityUploadRequest
            {
                InputStream = stream,
                Key = name,
                BucketName = _bucket,
                ContentType = AmazonS3Util.MimeTypeFromExtension(Path.GetExtension(name))
            };
            if (_publicRead) request.CannedACL = S3CannedACL.PublicRead;
            return request;
        }
    }
}
