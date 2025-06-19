#include "makelib.h"
#include <stdio.h>
#include <stdlib.h>
#if defined(_WIN32) || defined(WIN32) || defined(WIN64)
#include <fcntl.h>
#include <io.h>
#endif

int main()
{
#if defined(_WIN32) || defined(WIN32) || defined(WIN64)
    _setmode(_fileno(stdout), _O_BINARY);
#endif

    // Create context for memory management
    bebop_context_t *context = bebop_context_create();
    if (!context)
    {
        fprintf(stderr, "Failed to create bebop context\n");
        return 1;
    }

    // Create the library
    library_t lib = make_library(context);

    // Get writer
    bebop_writer_t writer;
    if (bebop_context_get_writer(context, &writer) != BEBOP_OK)
    {
        fprintf(stderr, "Failed to get writer\n");
        bebop_context_destroy(context);
        return 1;
    }

    // Encode the library
    if (library_encode_into(&lib, &writer) != BEBOP_OK)
    {
        fprintf(stderr, "Failed to encode library\n");
        bebop_context_destroy(context);
        return 1;
    }

    // Get the buffer and write to stdout
    uint8_t *buffer;
    size_t buffer_length;
    if (bebop_writer_get_buffer(&writer, &buffer, &buffer_length) != BEBOP_OK)
    {
        fprintf(stderr, "Failed to get buffer\n");
        bebop_context_destroy(context);
        return 1;
    }

    fwrite(buffer, sizeof(uint8_t), buffer_length, stdout);

    bebop_context_destroy(context);
    return 0;
}