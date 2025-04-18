using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using Azure.Storage.Blobs;
using Path = System.IO.Path;
using AttiePhotoUploader;

const string imagesPath =
    @"C:\Users\adamh\Transfers\Exports\attie.co\";

const string galleryName = "dwest-basketball-2025-senior-night";

// Build configuration to retrieve secrets
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>() // Load secrets from user secrets
    .Build();

string thumbnailsPath = Path.Combine(imagesPath, "thumbnails");
Directory.CreateDirectory(thumbnailsPath);

// Delete all the files in the thumbnails folder
if (Directory.Exists(thumbnailsPath))
{
    foreach (var file in Directory.EnumerateFiles(thumbnailsPath, "*.*", SearchOption.AllDirectories))
    {
        File.Delete(file);
    }
}

var counter = 0;
// For each image in the folder, process it using ThumbnailProcessor
foreach (var file in Directory.EnumerateFiles(imagesPath, $"*.png", SearchOption.TopDirectoryOnly))
{
    counter++;
    ImageProcessor.ProcessImage(file, thumbnailsPath, counter);
}

// Retrieve Azure Storage connection string and container name from secrets
var azureStorageConnectionString = configuration["AzureStorageConnectionString"];
var containerName = configuration["AzureContainerName"];
if (string.IsNullOrEmpty(azureStorageConnectionString) || string.IsNullOrEmpty(containerName))
{
    throw new InvalidOperationException("Azure Storage connection string or container name is not set in the user secrets.");
}

// Initialize BlobServiceClient and BlobContainerClient
var blobServiceClient = new BlobServiceClient(azureStorageConnectionString);
var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);

// Ensure the container exists
await blobContainerClient.CreateIfNotExistsAsync();

// Upload all files in the imagesPath folder (including thumbnails) to Azure Blob Storage
static async Task UploadFolderToBlobStorage(string folderPath, BlobContainerClient containerClient, string galleryName)
{
    // Delete all blobs in the container that start with 'galleries' and the gallery name
    var blobsToDelete = containerClient.GetBlobs(prefix: $"galleries/{galleryName}").Select(b => b.Name).ToList();
    foreach (var blobName in blobsToDelete)
    {
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync();
        Console.WriteLine($"Deleted {blobName} from Azure Blob Storage.");
    }

    foreach (var file in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories))
    {
        // Prepend 'galleries' and the gallery name to the blob name
        var relativePath = Path.GetRelativePath(imagesPath, file).Replace("\\", "/");
        var blobName = $"galleries/{galleryName}/{relativePath}";
        var blobClient = containerClient.GetBlobClient(blobName);

        using var fileStream = File.OpenRead(file);
        await blobClient.UploadAsync(fileStream, overwrite: true);
        Console.WriteLine($"Uploaded {blobName} to Azure Blob Storage.");
    }
}

// Call the method to upload the entire folder
await UploadFolderToBlobStorage(imagesPath, blobContainerClient, galleryName);

