using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using Path = System.IO.Path;

namespace AttiePhotoUploader;

public static class ImageProcessor
{
    public static void ProcessImage(string filePath, string thumbnailsPath,
        int counter)
    {
        using var image = Image.Load(filePath);

        var referenceNo = Guid.NewGuid().ToString("N")[..5];
        var newFileName = $"attie-{counter:D3}-{referenceNo}.webp";

        // Add reference number as text with a black border in the bottom-right corner of the original image
        var font = SystemFonts.CreateFont("Segoe UI", 36, FontStyle.Bold);
        var textSize = TextMeasurer.MeasureBounds(referenceNo, new(font)).Size;
        var location = new PointF(image.Width - textSize.X - 20,
            image.Height - textSize.Y -
            20); // Offset 20 points from bottom and right

        image.Mutate(ctx =>
        {
            // Draw the border (black text slightly offset in all directions)
            ctx.DrawText(referenceNo, font, Color.Black,
                new(location.X - 1, location.Y - 1));
            ctx.DrawText(referenceNo, font, Color.Black,
                new(location.X + 1, location.Y - 1));
            ctx.DrawText(referenceNo, font, Color.Black,
                new(location.X - 1, location.Y + 1));
            ctx.DrawText(referenceNo, font, Color.Black,
                new(location.X + 1, location.Y + 1));

            // Draw the main text (white text on top)
            ctx.DrawText(referenceNo, font, Color.White, location);
        });

        // Save the original image as a webp locally
        var originalFilePath =
            Path.Combine(Path.GetDirectoryName(filePath)!, newFileName);
        image.SaveAsWebp(originalFilePath);

        // Generate the thumbnail
        GenerateThumbnail(filePath, thumbnailsPath, newFileName);

        // Log the file paths
        Console.WriteLine($"Processed: {originalFilePath}");

        // Delete the original file
        File.Delete(filePath);
    }

    private static void GenerateThumbnail(string filePath,
        string thumbnailsPath, string newfileName)
    {
        using var image = Image.Load(filePath);

        var width = image.Width;
        var height = image.Height;

        var newWidth = Math.Min(width, height);
        var newHeight = Math.Min(width, height);
        var newStartX = (width - newWidth) / 2;
        var newStartY = (height - newHeight) / 2;

        // Crop the image to a square
        var rectangle =
            new Rectangle(newStartX, newStartY, newWidth, newHeight);
        image.Mutate(x => x.Crop(rectangle));

        // Save the cropped image as a thumbnail locally
        var outputFilePath = Path.Combine(thumbnailsPath, newfileName);
        image.SaveAsWebp(outputFilePath);

        Console.WriteLine($"Thumbnail generated: {outputFilePath}");
    }
}