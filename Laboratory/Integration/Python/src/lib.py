from schema import Library, Instrument, Album, StudioAlbum, LiveAlbum, Song, Musician
from datetime import datetime, timezone
from uuid import UUID

def make_lib():
    giant_steps_song = Song()
    giant_steps_song.title = "Giant Steps"
    giant_steps_song.title = "Giant Steps"
    giant_steps_song.year = 1959
    giant_steps_song.performers = [
        Musician(name="John Coltrane", plays=Instrument.PIANO, id=UUID("ff990458-a276-4b71-b2e3-57d49470b949"))
    ]

    a_night_in_tunisia_song = Song()
    a_night_in_tunisia_song.title = "A Night in Tunisia"
    a_night_in_tunisia_song.year = 1942
    a_night_in_tunisia_song.performers = [
        Musician(name="Dizzy Gillespie", plays=Instrument.TRUMPET, id=UUID("84f4b320-0f1e-463e-982c-78772fabd74d")),
        Musician(name="Count Basie", plays=Instrument.PIANO, id=UUID("b28d54d6-a3f7-48bf-a07a-117c15cf33ef")),
    ]

    groovin_high_song = Song()
    groovin_high_song.title = "Groovin' High"

    adams_apple_album = LiveAlbum()
    adams_apple_album.venueName = "Tunisia"
    adams_apple_album.concertDate = datetime.fromtimestamp(528205479, tz=timezone.utc)

    unnamed_song = Song()
    unnamed_song.year = 1965
    unnamed_song.performers = [
        Musician(name="Carmell Jones", plays=Instrument.TRUMPET, id=UUID("f7c31724-0387-4ac9-b6f0-361bb9415c1b")),
        Musician(name="Joe Henderson", plays=Instrument.SAX, id=UUID("bb4facf3-c65a-46dd-a96f-73ca6d1cf3f6")),
        Musician(name="Teddy Smith", plays=Instrument.CLARINET, id=UUID("91ffb47f-2a38-4876-8186-1f267cc21706"))
    ]

    brilliant_corners_album = LiveAlbum()
    brilliant_corners_album.venueName = "Night's Palace"
    brilliant_corners_album.tracks = [
        unnamed_song
    ]

    return Library(albums={
        "Giant Steps": Album.fromStudioAlbum(StudioAlbum(tracks=[
            giant_steps_song, a_night_in_tunisia_song, groovin_high_song
        ])),
        "Adam's Apple": Album.fromLiveAlbum(adams_apple_album),
        "Milestones": Album.fromStudioAlbum(StudioAlbum(tracks=[])),
        "Brilliant Corners": Album.fromLiveAlbum(brilliant_corners_album)
    })