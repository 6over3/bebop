using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Bebop.Runtime;

if (args.Length == 1 && args[0] == "encode")
{
    var lib = MakeLibrary();
    byte[] buffer = Library.Encode(lib);

    // Write to stdout for piping
    using var stdout = Console.OpenStandardOutput();
    stdout.Write(buffer, 0, buffer.Length);
    stdout.Flush();

    return 0;
}
else if (args.Length == 2 && args[0] == "encode")
{
    var lib = MakeLibrary();
    byte[] buffer = Library.Encode(lib);
    var path = Path.GetFullPath(args[1]);
    Console.WriteLine($"Writing {buffer.Length} bytes to {path}");
    File.WriteAllBytes(path, buffer);

    return 0;
}
else if (args.Length == 1 && args[0] == "decode")
{
    // Read from stdin
    using var stdin = Console.OpenStandardInput();
    using var memoryStream = new MemoryStream();
    stdin.CopyTo(memoryStream);
    byte[] buffer = memoryStream.ToArray();

    var lib = Library.Decode(buffer);
    ValidateLibrary(lib);
    return 0;
}
else if (args.Length == 2 && args[0] == "decode")
{
    byte[] buffer = File.ReadAllBytes(args[1]);
    var lib = Library.Decode(buffer);
    ValidateLibrary(lib);
    return 0;
}
else
{
    var name = Environment.GetCommandLineArgs()[0];
    Console.WriteLine($"Usage:");
    Console.WriteLine($"  {name} encode              # encode to stdout");
    Console.WriteLine($"  {name} encode file.enc     # encode to file");
    Console.WriteLine($"  {name} decode              # decode from stdin");
    Console.WriteLine($"  {name} decode file.enc     # decode from file");
    return 1;
}

static Library MakeLibrary()
{
    return new Library(
                new Dictionary<string, Album>
                {
                    ["Giant Steps"] = new StudioAlbum(
                        new[]
                        {
                            new Song("Giant Steps", 1959,
                                new[] { new Musician("John Coltrane", Instrument.Piano, Guid.Parse("ff990458-a276-4b71-b2e3-57d49470b949")) }),
                            new Song("A Night in Tunisia", 1942,
                                new[]
                                {
                                    new Musician("Dizzy Gillespie", Instrument.Trumpet, Guid.Parse("84f4b320-0f1e-463e-982c-78772fabd74d")),
                                    new Musician("Count Basie", Instrument.Piano, Guid.Parse("b28d54d6-a3f7-48bf-a07a-117c15cf33ef"))
                                }),
                            new Song("Groovin' High", null, null)
                        }
                    ),
                    ["Adam's Apple"] = new LiveAlbum(null, "Tunisia", DateTime.FromFileTimeUtc(121726790790000000)),
                    ["Milestones"] = new StudioAlbum(Array.Empty<Song>()),
                    ["Brilliant Corners"] = new LiveAlbum(
                        new[]
                        {
                            new Song(null, 1965,
                                new[]
                                {
                                    new Musician("Carmell Jones", Instrument.Trumpet, Guid.Parse("f7c31724-0387-4ac9-b6f0-361bb9415c1b")),
                                    new Musician("Joe Henderson", Instrument.Sax, Guid.Parse("bb4facf3-c65a-46dd-a96f-73ca6d1cf3f6")),
                                    new Musician("Teddy Smith", Instrument.Clarinet, Guid.Parse("91ffb47f-2a38-4876-8186-1f267cc21706"))
                                })
                        }, "Night's Palace", null)
                });
}

static void ValidateLibrary(Library lib)
{
    Debug.Assert(lib.Albums.Count == 4);
    {
        var album = lib.Albums["Giant Steps"].AsStudioAlbum;
        Debug.Assert(album.Tracks.Length == 3);
        {
            var track = album.Tracks[0];
            Debug.Assert(track.Title == "Giant Steps");
            Debug.Assert(track.Year == 1959);
            {
                var performers = track.Performers;
                Debug.Assert(performers?.Length == 1);
                Debug.Assert(performers[0].Name == "John Coltrane");
                Debug.Assert(performers[0].Plays == Instrument.Piano);
                Debug.Assert(performers[0].ID == Guid.Parse("ff990458-a276-4b71-b2e3-57d49470b949"));
            }
        }
        {
            var track = album.Tracks[1];
            Debug.Assert(track.Title == "A Night in Tunisia");
            Debug.Assert(track.Year == 1942);
            {
                var performers = track.Performers;
                Debug.Assert(performers?.Length == 2);
                Debug.Assert(performers[0].Name == "Dizzy Gillespie");
                Debug.Assert(performers[0].Plays == Instrument.Trumpet);
                Debug.Assert(performers[0].ID == Guid.Parse("84f4b320-0f1e-463e-982c-78772fabd74d"));
                Debug.Assert(performers[1].Name == "Count Basie");
                Debug.Assert(performers[1].Plays == Instrument.Piano);
                Debug.Assert(performers[1].ID == Guid.Parse("b28d54d6-a3f7-48bf-a07a-117c15cf33ef"));
            }
        }
        {
            var track = album.Tracks[2];
            Debug.Assert(track.Title == "Groovin' High");
            Debug.Assert(track.Year is null);
            Debug.Assert(track.Performers is null);
        }
    }
    {
        var album = lib.Albums["Adam's Apple"].AsLiveAlbum;
        Debug.Assert(album.Tracks is null);
        Debug.Assert(album.VenueName == "Tunisia");
        Debug.Assert(album.ConcertDate == DateTime.FromFileTimeUtc(121726790790000000));
    }
    {
        var album = lib.Albums["Milestones"].AsStudioAlbum;
        Debug.Assert(album.Tracks.Length == 0);
    }
    {
        var album = lib.Albums["Brilliant Corners"].AsLiveAlbum;
        Debug.Assert(album.VenueName == "Night's Palace");
        Debug.Assert(album.ConcertDate is null);
        var tracks = album.Tracks;
        Debug.Assert(tracks?.Length == 1);
        var track = tracks[0];
        Debug.Assert(track.Title is null);
        Debug.Assert(track.Year == 1965);
        var performers = track.Performers;
        Debug.Assert(performers?.Length == 3);
        Debug.Assert(performers[0].Name == "Carmell Jones");
        Debug.Assert(performers[0].Plays == Instrument.Trumpet);
        Debug.Assert(performers[0].ID == Guid.Parse("f7c31724-0387-4ac9-b6f0-361bb9415c1b"));
        Debug.Assert(performers[1].Name == "Joe Henderson");
        Debug.Assert(performers[1].Plays == Instrument.Sax);
        Debug.Assert(performers[1].ID == Guid.Parse("bb4facf3-c65a-46dd-a96f-73ca6d1cf3f6"));
        Debug.Assert(performers[2].Name == "Teddy Smith");
        Debug.Assert(performers[2].Plays == Instrument.Clarinet);
        Debug.Assert(performers[2].ID == Guid.Parse("91ffb47f-2a38-4876-8186-1f267cc21706"));
    }
}