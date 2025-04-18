using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;               // for image.Mutate
using SixLabors.ImageSharp.Drawing.Processing;       // for DrawText, DrawingOptions, RichTextOptions
using SixLabors.Fonts;                               // for Font, SystemFonts
using MongoDB.Bson;
using MongoDB.Driver;
using Azure.Storage.Blobs;
using Path = System.IO.Path;

const string imagesPath =
    @"C:\Users\adamh\Transfers\Exports\attie.co\MarshCreek\";

const string galleryName = "marsh-creek";

// Build configuration to retrieve secrets
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>() // Load secrets from user secrets
    .Build();

// Retrieve MongoDB connection string from secrets
var connectionString = configuration["MongoDbConnectionString"];
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("MongoDB connection string is not set in the user secrets.");
}

// Initialize MongoDB client and collection
var client = new MongoClient(connectionString);
var database = client.GetDatabase("attieco");
var collection = database.GetCollection<BsonDocument>("images");

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

// Find out the extension of the first image in the folder
var firstImageFile = Directory.EnumerateFiles(imagesPath, "*.*", SearchOption.TopDirectoryOnly).FirstOrDefault() ??
    throw new FileNotFoundException("No images found in the specified folder.");

var firstImageExtension = Path.GetExtension(firstImageFile).TrimStart('.').ToLowerInvariant();

// Delete all the Documents in the collection that have the same gallery name
var filter = Builders<BsonDocument>.Filter.Eq("gallery", galleryName);
await collection.DeleteManyAsync(filter);

// For each image in the folder, crop it to a square, add reference number with a border, and save it as a webp
foreach (var file in Directory.EnumerateFiles(imagesPath, $"*.{firstImageExtension}", SearchOption.TopDirectoryOnly))
{
    using var image = Image.Load(file);

    var referenceNo = Guid.NewGuid().ToString("N")[..5];

    var width = image.Width;
    var height = image.Height;

    var newWidth = Math.Min(width, height);
    var newHeight = Math.Min(width, height);
    var newStartX = (width - newWidth) / 2;
    var newStartY = (height - newHeight) / 2;

    // Add reference number as text with a black border on the bottom-right corner of the original image
    var font = SystemFonts.CreateFont("Arial", 24, FontStyle.Bold);
    var textSize = TextMeasurer.MeasureBounds(referenceNo, new TextOptions(font)).Size;
    var location = new PointF(width - textSize.X - 10, height - textSize.Y - 10); // Bottom-right with padding

    image.Mutate(ctx =>
    {
        // Draw the border (black text slightly offset in all directions)
        ctx.DrawText(referenceNo, font, Color.Black, new PointF(location.X - 1, location.Y - 1));
        ctx.DrawText(referenceNo, font, Color.Black, new PointF(location.X + 1, location.Y - 1));
        ctx.DrawText(referenceNo, font, Color.Black, new PointF(location.X - 1, location.Y + 1));
        ctx.DrawText(referenceNo, font, Color.Black, new PointF(location.X + 1, location.Y + 1));

        // Draw the main text (white text on top)
        ctx.DrawText(referenceNo, font, Color.White, location);
    });

    // Overwrite the original image with the new image
    image.Save(file);
    Console.WriteLine($"Saved image with reference number: {referenceNo}");

    // Crop the image to a square
    var rectangle = new Rectangle(newStartX, newStartY, newWidth, newHeight);
    image.Mutate(x => x.Crop(rectangle));

    // Save the cropped image as a thumbnail locally
    var outputFilePath = Path.Combine(thumbnailsPath, Path.GetFileNameWithoutExtension(file) + ".webp");
    image.SaveAsWebp(outputFilePath);
    Console.WriteLine($"Saved thumbnail locally: {outputFilePath}");

    // Create MongoDB entry
    var document = new BsonDocument
    {
        { "filename", Path.GetFileName(outputFilePath) },
        { "ref",  referenceNo },
        { "gallery", galleryName }
    };
    collection.InsertOne(document);
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

