using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;

namespace Menus.Files.Services
{
    public class DigitalOceanService
    {
        private readonly AmazonS3Client _client;
        private readonly TransferUtility _do;

        public DigitalOceanService()
        {
            _client = new AmazonS3Client("WP3GSBREDVZEGCOEE2R2", "LpPMcrd30TwfzlKJ+LXf7VuUr7aG2G2g4eDH3Vchkrg", new AmazonS3Config
            {
                ServiceURL = "https://fra1.digitaloceanspaces.com"
            });

            _do = new TransferUtility(_client);
        }

        ~DigitalOceanService()
        {
            _client.Dispose();
        }

        public async Task UploadFile(byte[] file, string name)
        {
            using (var memoryStream = new MemoryStream(file))
            {
                var uploadRequest = new TransferUtilityUploadRequest
                {
                    InputStream = memoryStream,
                    Key = name,
                    BucketName = "cdn-tipmymenus-com",
                    CannedACL = S3CannedACL.PublicRead
                };

                await _do.UploadAsync(uploadRequest);
            }
        }

        public async Task UploadFile(string filePath, string name)
        {
            using (var fileStream = File.OpenRead(filePath))
            {
                var uploadRequest = new TransferUtilityUploadRequest
                {
                    InputStream = fileStream,
                    Key = name,
                    BucketName = "cdn-tipmymenus-com",
                    CannedACL = S3CannedACL.PublicRead
                };

                await _do.UploadAsync(uploadRequest);
            }
        }
    }
}
