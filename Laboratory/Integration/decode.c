#include "makelib.h"
#include <stdio.h>
#include <stdlib.h>

static uint8_t *read_all_bytes(const char *filename, size_t *out_size)
{
    FILE *file = fopen(filename, "rb");
    if (!file)
    {
        fprintf(stderr, "Failed to open file: %s\n", filename);
        return NULL;
    }

    // Get file size
    fseek(file, 0, SEEK_END);
    long file_size = ftell(file);
    if (file_size < 0)
    {
        fprintf(stderr, "Failed to get file size\n");
        fclose(file);
        return NULL;
    }
    fseek(file, 0, SEEK_SET);

    // Allocate buffer
    uint8_t *buffer = malloc(file_size);
    if (!buffer)
    {
        fprintf(stderr, "Failed to allocate buffer\n");
        fclose(file);
        return NULL;
    }

    // Read file
    size_t bytes_read = fread(buffer, 1, file_size, file);
    if (bytes_read != (size_t)file_size)
    {
        fprintf(stderr, "Failed to read entire file\n");
        free(buffer);
        fclose(file);
        return NULL;
    }

    fclose(file);
    *out_size = file_size;
    return buffer;
}

int main(int argc, char **argv)
{
    if (argc != 2)
    {
        fprintf(stderr, "Usage: %s <filename>\n", argv[0]);
        return 1;
    }

    // Read the file
    size_t buffer_size;
    uint8_t *buffer = read_all_bytes(argv[1], &buffer_size);
    if (!buffer)
    {
        return 1;
    }

    // Create context
    bebop_context_t *context = bebop_context_create();
    if (!context)
    {
        fprintf(stderr, "Failed to create bebop context\n");
        free(buffer);
        return 1;
    }

    // Create reader
    bebop_reader_t reader;
    if (bebop_context_get_reader(context, buffer, buffer_size, &reader) != BEBOP_OK)
    {
        fprintf(stderr, "Failed to create reader\n");
        free(buffer);
        bebop_context_destroy(context);
        return 1;
    }

    // Decode library
    library_t lib;
    if (library_decode_into(&reader, &lib) != BEBOP_OK)
    {
        fprintf(stderr, "Failed to decode library\n");
        free(buffer);
        bebop_context_destroy(context);
        return 1;
    }

    // Validate the library
    is_valid(&lib);

    // Cleanup
    free(buffer);
    bebop_context_destroy(context);
    return 0;
}