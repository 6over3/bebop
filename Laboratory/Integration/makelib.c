#include "makelib.h"

library_t make_library(bebop_context_t *context)
{
    library_t lib = {0};

    // Create the albums map with 4 entries
    lib.albums.length = 4;
    lib.albums.entries = (bebop_string_view_album_map_entry_t *)bebop_context_alloc(
        context, sizeof(bebop_string_view_album_map_entry_t) * 4);
    assert(lib.albums.entries != NULL);

    // Giant Steps - Studio Album
    {
        lib.albums.entries[0].key = bebop_string_view_from_cstr("Giant Steps");
        album_t *album = &lib.albums.entries[0].value;
        album->tag = 1; // StudioAlbum tag

        studio_album_t *studio = &album->as_studio_album;
        studio->tracks.length = 3;
        studio->tracks.data = (song_t *)bebop_context_alloc(context, sizeof(song_t) * 3);
        assert(studio->tracks.data != NULL);

        // Song 1: Giant Steps
        song_t *song1 = &studio->tracks.data[0];
        *song1 = (song_t){0};
        bebop_assign_some(song1->title, bebop_string_view_from_cstr("Giant Steps"));
        bebop_assign_some(song1->year, (uint16_t)1959);

        bebop_musician_array_t performers1;
        performers1.length = 1;
        performers1.data = (musician_t *)bebop_context_alloc(context, sizeof(musician_t));
        assert(performers1.data != NULL);

        musician_t m1 = {
            .name = bebop_string_view_from_cstr("John Coltrane"),
            .plays = INSTRUMENT_PIANO,
            .id = bebop_guid_from_string("ff990458-a276-4b71-b2e3-57d49470b949")};
        memcpy(&performers1.data[0], &m1, sizeof(musician_t));
        bebop_assign_some(song1->performers, performers1);

        // Song 2: A Night in Tunisia
        song_t *song2 = &studio->tracks.data[1];
        *song2 = (song_t){0};
        bebop_assign_some(song2->title, bebop_string_view_from_cstr("A Night in Tunisia"));
        bebop_assign_some(song2->year, (uint16_t)1942);

        bebop_musician_array_t performers2;
        performers2.length = 2;
        performers2.data = (musician_t *)bebop_context_alloc(context, sizeof(musician_t) * 2);
        assert(performers2.data != NULL);

        musician_t m2a = {
            .name = bebop_string_view_from_cstr("Dizzy Gillespie"),
            .plays = INSTRUMENT_TRUMPET,
            .id = bebop_guid_from_string("84f4b320-0f1e-463e-982c-78772fabd74d")};
        musician_t m2b = {
            .name = bebop_string_view_from_cstr("Count Basie"),
            .plays = INSTRUMENT_PIANO,
            .id = bebop_guid_from_string("b28d54d6-a3f7-48bf-a07a-117c15cf33ef")};
        memcpy(&performers2.data[0], &m2a, sizeof(musician_t));
        memcpy(&performers2.data[1], &m2b, sizeof(musician_t));
        bebop_assign_some(song2->performers, performers2);

        // Song 3: Groovin' High (with no optional fields set)
        song_t *song3 = &studio->tracks.data[2];
        *song3 = (song_t){0};
        bebop_assign_some(song3->title, bebop_string_view_from_cstr("Groovin' High"));
        // year and performers remain unset (none)
    }

    // Adam's Apple - Live Album
    {
        lib.albums.entries[1].key = bebop_string_view_from_cstr("Adam's Apple");
        album_t *album = &lib.albums.entries[1].value;
        album->tag = 2; // LiveAlbum tag

        live_album_t *live = &album->as_live_album;
        *live = (live_album_t){0};
        // tracks remains unset (none)
        bebop_assign_some(live->venue_name, bebop_string_view_from_cstr("Tunisia"));
        bebop_assign_some(live->concert_date, (bebop_date_t){5282054790000000});
    }

    // Milestones - Studio Album (empty tracks)
    {
        lib.albums.entries[2].key = bebop_string_view_from_cstr("Milestones");
        album_t *album = &lib.albums.entries[2].value;
        album->tag = 1; // StudioAlbum tag

        studio_album_t *studio = &album->as_studio_album;
        studio->tracks.length = 0;
        studio->tracks.data = NULL;
    }

    // Brilliant Corners - Live Album
    {
        lib.albums.entries[3].key = bebop_string_view_from_cstr("Brilliant Corners");
        album_t *album = &lib.albums.entries[3].value;
        album->tag = 2; // LiveAlbum tag

        live_album_t *live = &album->as_live_album;
        *live = (live_album_t){0};

        bebop_song_array_t tracks;
        tracks.length = 1;
        tracks.data = (song_t *)bebop_context_alloc(context, sizeof(song_t));
        assert(tracks.data != NULL);

        song_t *song = &tracks.data[0];
        *song = (song_t){0};
        // title remains unset (none)
        bebop_assign_some(song->year, (uint16_t)1965);

        bebop_musician_array_t performers;
        performers.length = 3;
        performers.data = (musician_t *)bebop_context_alloc(context, sizeof(musician_t) * 3);
        assert(performers.data != NULL);

        musician_t m3a = {
            .name = bebop_string_view_from_cstr("Carmell Jones"),
            .plays = INSTRUMENT_TRUMPET,
            .id = bebop_guid_from_string("f7c31724-0387-4ac9-b6f0-361bb9415c1b")};
        musician_t m3b = {
            .name = bebop_string_view_from_cstr("Joe Henderson"),
            .plays = INSTRUMENT_SAX,
            .id = bebop_guid_from_string("bb4facf3-c65a-46dd-a96f-73ca6d1cf3f6")};
        musician_t m3c = {
            .name = bebop_string_view_from_cstr("Teddy Smith"),
            .plays = INSTRUMENT_CLARINET,
            .id = bebop_guid_from_string("91ffb47f-2a38-4876-8186-1f267cc21706")};
        memcpy(&performers.data[0], &m3a, sizeof(musician_t));
        memcpy(&performers.data[1], &m3b, sizeof(musician_t));
        memcpy(&performers.data[2], &m3c, sizeof(musician_t));
        bebop_assign_some(song->performers, performers);

        bebop_assign_some(live->tracks, tracks);
        bebop_assign_some(live->venue_name, bebop_string_view_from_cstr("Night's Palace"));
        // concert_date remains unset (none)
    }

    return lib;
}

static album_t *find_album(const library_t *lib, const char *name)
{
    bebop_string_view_t name_view = bebop_string_view_from_cstr(name);
    for (size_t i = 0; i < lib->albums.length; i++)
    {
        if (bebop_string_view_equal(lib->albums.entries[i].key, name_view))
        {
            return &lib->albums.entries[i].value;
        }
    }
    return NULL;
}

void is_valid(const library_t *lib)
{
    assert(lib->albums.length == 4);

    // Giant Steps validation
    {
        album_t *album = find_album(lib, "Giant Steps");
        assert(album != NULL);
        assert(album->tag == 1); // StudioAlbum

        studio_album_t *studio = &album->as_studio_album;
        assert(studio->tracks.length == 3);

        // Track 1: Giant Steps
        {
            song_t *track = &studio->tracks.data[0];
            assert(bebop_is_some(track->title));
            bebop_string_view_t title = bebop_unwrap(track->title);
            assert(bebop_string_view_equal(title, bebop_string_view_from_cstr("Giant Steps")));

            assert(bebop_is_some(track->year));
            assert(bebop_unwrap(track->year) == 1959);

            assert(bebop_is_some(track->performers));
            bebop_musician_array_t performers = bebop_unwrap(track->performers);
            assert(performers.length == 1);
            assert(bebop_string_view_equal(performers.data[0].name, bebop_string_view_from_cstr("John Coltrane")));
            assert(performers.data[0].plays == INSTRUMENT_PIANO);
            assert(bebop_guid_equal(performers.data[0].id, bebop_guid_from_string("ff990458-a276-4b71-b2e3-57d49470b949")));
        }

        // Track 2: A Night in Tunisia
        {
            song_t *track = &studio->tracks.data[1];
            assert(bebop_is_some(track->title));
            bebop_string_view_t title = bebop_unwrap(track->title);
            assert(bebop_string_view_equal(title, bebop_string_view_from_cstr("A Night in Tunisia")));

            assert(bebop_is_some(track->year));
            assert(bebop_unwrap(track->year) == 1942);

            assert(bebop_is_some(track->performers));
            bebop_musician_array_t performers = bebop_unwrap(track->performers);
            assert(performers.length == 2);
            assert(bebop_string_view_equal(performers.data[0].name, bebop_string_view_from_cstr("Dizzy Gillespie")));
            assert(performers.data[0].plays == INSTRUMENT_TRUMPET);
            assert(bebop_guid_equal(performers.data[0].id, bebop_guid_from_string("84f4b320-0f1e-463e-982c-78772fabd74d")));
            assert(bebop_string_view_equal(performers.data[1].name, bebop_string_view_from_cstr("Count Basie")));
            assert(performers.data[1].plays == INSTRUMENT_PIANO);
            assert(bebop_guid_equal(performers.data[1].id, bebop_guid_from_string("b28d54d6-a3f7-48bf-a07a-117c15cf33ef")));
        }

        // Track 3: Groovin' High
        {
            song_t *track = &studio->tracks.data[2];
            assert(bebop_is_some(track->title));
            bebop_string_view_t title = bebop_unwrap(track->title);
            assert(bebop_string_view_equal(title, bebop_string_view_from_cstr("Groovin' High")));
            assert(!bebop_is_some(track->year));
            assert(!bebop_is_some(track->performers));
        }
    }

    // Adam's Apple validation
    {
        album_t *album = find_album(lib, "Adam's Apple");
        assert(album != NULL);
        assert(album->tag == 2); // LiveAlbum

        live_album_t *live = &album->as_live_album;
        assert(!bebop_is_some(live->tracks));
        assert(bebop_is_some(live->venue_name));
        bebop_string_view_t venue = bebop_unwrap(live->venue_name);
        assert(bebop_string_view_equal(venue, bebop_string_view_from_cstr("Tunisia")));
        assert(bebop_is_some(live->concert_date));
        assert(bebop_unwrap(live->concert_date) == 5282054790000000);
    }

    // Milestones validation
    {
        album_t *album = find_album(lib, "Milestones");
        assert(album != NULL);
        assert(album->tag == 1); // StudioAlbum

        studio_album_t *studio = &album->as_studio_album;
        assert(studio->tracks.length == 0);
    }

    // Brilliant Corners validation
    {
        album_t *album = find_album(lib, "Brilliant Corners");
        assert(album != NULL);
        assert(album->tag == 2); // LiveAlbum

        live_album_t *live = &album->as_live_album;
        assert(bebop_is_some(live->venue_name));
        bebop_string_view_t venue = bebop_unwrap(live->venue_name);
        assert(bebop_string_view_equal(venue, bebop_string_view_from_cstr("Night's Palace")));
        assert(!bebop_is_some(live->concert_date));

        assert(bebop_is_some(live->tracks));
        bebop_song_array_t tracks = bebop_unwrap(live->tracks);
        assert(tracks.length == 1);

        song_t *track = &tracks.data[0];
        assert(!bebop_is_some(track->title));
        assert(bebop_is_some(track->year));
        assert(bebop_unwrap(track->year) == 1965);

        assert(bebop_is_some(track->performers));
        bebop_musician_array_t performers = bebop_unwrap(track->performers);
        assert(performers.length == 3);
        assert(bebop_string_view_equal(performers.data[0].name, bebop_string_view_from_cstr("Carmell Jones")));
        assert(performers.data[0].plays == INSTRUMENT_TRUMPET);
        assert(bebop_guid_equal(performers.data[0].id, bebop_guid_from_string("f7c31724-0387-4ac9-b6f0-361bb9415c1b")));
        assert(bebop_string_view_equal(performers.data[1].name, bebop_string_view_from_cstr("Joe Henderson")));
        assert(performers.data[1].plays == INSTRUMENT_SAX);
        assert(bebop_guid_equal(performers.data[1].id, bebop_guid_from_string("bb4facf3-c65a-46dd-a96f-73ca6d1cf3f6")));
        assert(bebop_string_view_equal(performers.data[2].name, bebop_string_view_from_cstr("Teddy Smith")));
        assert(performers.data[2].plays == INSTRUMENT_CLARINET);
        assert(bebop_guid_equal(performers.data[2].id, bebop_guid_from_string("91ffb47f-2a38-4876-8186-1f267cc21706")));
    }
}