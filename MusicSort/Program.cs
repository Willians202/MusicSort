using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TagLib;
using System.Windows.Forms;

class Program
{
    static void Main()
    {
        Console.WriteLine("Ingrese el directorio fuente:");
        string sourceDirectory = Console.ReadLine();

        Console.WriteLine("Ingrese el directorio destino:");
        string destinationDirectory = Console.ReadLine();

        Console.WriteLine("Ingrese el directorio para guardar las capturas de pantalla:");
        string screenshotDirectory = Console.ReadLine();

        OrganizeMusicFiles(sourceDirectory, destinationDirectory, screenshotDirectory).Wait();

        Console.WriteLine("Organización de archivos de música finalizada.");
        Console.WriteLine("Ruta de las capturas de pantalla:");
        Console.WriteLine(screenshotDirectory);
        Console.WriteLine("Presione Enter para finalizar.");
        Console.ReadLine();
    }

    static async Task OrganizeMusicFiles(string sourceDirectory, string destinationDirectory, string screenshotDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            Console.WriteLine("El directorio fuente no existe.");
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        Directory.CreateDirectory(screenshotDirectory);

        string[] musicFiles = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                           file.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int numThreads = Environment.ProcessorCount;
        ManualResetEvent[] doneEvents = new ManualResetEvent[numThreads];

        for (int i = 0; i < numThreads; i++)
        {
            doneEvents[i] = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(OrganizeMusicFile, new OrganizeMusicFileParams(musicFiles, i, numThreads, destinationDirectory, screenshotDirectory, doneEvents[i]));
        }

        await Task.WhenAll(doneEvents.Select(doneEvent => Task.Run(() => doneEvent.WaitOne())));
    }

    static void OrganizeMusicFile(object state)
    {
        OrganizeMusicFileParams parameters = (OrganizeMusicFileParams)state;
        string[] musicFiles = parameters.MusicFiles;
        int startIndex = parameters.StartIndex;
        int step = parameters.Step;
        string destinationDirectory = parameters.DestinationDirectory;
        string screenshotDirectory = parameters.ScreenshotDirectory;
        ManualResetEvent doneEvent = parameters.DoneEvent;

        try
        {
            for (int i = startIndex; i < musicFiles.Length; i += step)
            {
                string musicFile = musicFiles[i];
                string fileName = Path.GetFileName(musicFile);

                var tagFile = TagLib.File.Create(musicFile);
                var tag = tagFile.GetTag(TagTypes.Id3v2);
                var tagLibProperties = tagFile.Properties;

                string artist = tag.AlbumArtists.FirstOrDefault();
                string year = tag.Year.ToString();
                string album = tag.Album;

                string artistDirectory = Path.Combine(destinationDirectory, ConfirmPath(artist));
                string yearDirectory = Path.Combine(artistDirectory, year);
                string albumDirectory = Path.Combine(yearDirectory, ConfirmPath(album));

                Directory.CreateDirectory(artistDirectory);
                Directory.CreateDirectory(yearDirectory);
                Directory.CreateDirectory(albumDirectory);

                string destinationFile = Path.Combine(albumDirectory, fileName);
                System.IO.File.Copy(musicFile, destinationFile, true);

                lock (Console.Out)
                {
                    Console.WriteLine("Archivo copiado correctamente:");
                    Console.WriteLine($"   Nombre del archivo: {fileName}");
                    Console.WriteLine($"   Ruta de destino: {destinationFile}");
                    Console.WriteLine();
                }

                // Tomar las capturas de pantalla de forma asincrónica
                TakeScreenshotsAsync(fileName, screenshotDirectory);

                // Salir del bucle si se ha completado el proceso de organización del archivo
                if (doneEvent.WaitOne(0))
                    break;
            }
        }
        catch (Exception ex)
        {
            lock (Console.Out)
            {
                Console.WriteLine("Error al leer los metadatos del archivo: " + ex.Message);
            }
        }

        doneEvent.Set();
    }

    static async Task TakeScreenshotsAsync(string fileName, string screenshotDirectory)
    {
        int screenshotCounter = 1;
        DateTime startTime = DateTime.Now;

        while (true)
        {
            using (var screenshot = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height))
            using (var graphics = Graphics.FromImage(screenshot))
            {
                graphics.CopyFromScreen(0, 0, 0, 0, screenshot.Size);
                string screenshotFileName = Path.Combine(screenshotDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}_screenshot{screenshotCounter}.png");
                screenshot.Save(screenshotFileName);
                screenshotCounter++;
            }

            // Esperar 30 segundos antes de tomar la siguiente captura de pantalla
            TimeSpan elapsedTime = DateTime.Now - startTime;
            if (elapsedTime.TotalSeconds >= 30)
                break;

            await Task.Delay(1000); // Esperar 1 segundo entre cada captura de pantalla
        }
    }

    static string ConfirmPath(string path)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string ConfirmPath = string.Join("_", path.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return ConfirmPath;
    }

    class OrganizeMusicFileParams
    {
        public string[] MusicFiles { get; }
        public int StartIndex { get; }
        public int Step { get; }
        public string DestinationDirectory { get; }
        public string ScreenshotDirectory { get; }
        public ManualResetEvent DoneEvent { get; }

        public OrganizeMusicFileParams(string[] musicFiles, int startIndex, int step, string destinationDirectory, string screenshotDirectory, ManualResetEvent doneEvent)
        {
            MusicFiles = musicFiles;
            StartIndex = startIndex;
            Step = step;
            DestinationDirectory = destinationDirectory;
            ScreenshotDirectory = screenshotDirectory;
            DoneEvent = doneEvent;
        }
    }
}
