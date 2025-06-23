#include "bebop.h"

// Internal utility functions
static size_t align_size(size_t size, size_t alignment) {
  return (size + alignment - 1) & ~(alignment - 1);
}

static bebop_arena_block_t *arena_allocate_block(const bebop_arena_t *arena,
                                                 size_t min_size) {
  size_t capacity = arena->options.initial_block_size;
  size_t required = align_size(min_size, BEBOP_ARENA_DEFAULT_ALIGNMENT);

  if (required > arena->options.max_block_size) 
    return NULL;
  if (capacity < required) 
    capacity = required;
  if (capacity > arena->options.max_block_size) 
    capacity = arena->options.max_block_size;

  size_t total_size = sizeof(bebop_arena_block_t) + capacity;

  bebop_arena_block_t *block =
      (bebop_arena_block_t *)arena->malloc_func(total_size);
  if (!block) {
    return NULL;
  }

  block->next = NULL;
  bebop_atomic_init(&block->used, 0);
  block->capacity = capacity;
  return block;
}

static bebop_arena_t *bebop_arena_create_with_options(
    const bebop_arena_options_t *options) {
  if (!options) return NULL;

  bebop_malloc_func_t malloc_func =
      options->allocator.malloc_func ? options->allocator.malloc_func : malloc;
  bebop_arena_t *arena = (bebop_arena_t *)malloc_func(sizeof(bebop_arena_t));
  if (!arena) return NULL;

  arena->options = *options;
  arena->malloc_func = malloc_func;
  arena->free_func =
      options->allocator.free_func ? options->allocator.free_func : free;
  bebop_atomic_init(&arena->current_block, NULL);
  bebop_atomic_init(&arena->total_allocated, 0);
  bebop_atomic_init(&arena->total_used, 0);

  return arena;
}

static void bebop_arena_destroy(bebop_arena_t *arena) {
  if (!arena) return;

  bebop_arena_block_t *block = bebop_atomic_load(&arena->current_block);
  while (block) {
    bebop_arena_block_t *next = block->next;
    arena->free_func(block);
    block = next;
  }

  arena->free_func(arena);
}

static void bebop_arena_reset(bebop_arena_t *arena) {
  if (!arena) return;

  bebop_arena_block_t *block = bebop_atomic_load(&arena->current_block);
  while (block) {
    bebop_arena_block_t *next = block->next;
    arena->free_func(block);
    block = next;
  }

  bebop_atomic_store(&arena->current_block, NULL);
  bebop_atomic_store(&arena->total_allocated, 0);
  bebop_atomic_store(&arena->total_used, 0);
}

// Arena allocation functions
void *bebop_arena_alloc(bebop_arena_t *arena, size_t size) {
  if (!arena || size == 0) return NULL;

  size_t aligned_size = align_size(size, BEBOP_ARENA_DEFAULT_ALIGNMENT);

  while (true) {
    bebop_arena_block_t *current = bebop_atomic_load(&arena->current_block);

    if (!current ||
        bebop_atomic_load(&current->used) + aligned_size > current->capacity) {
      bebop_arena_block_t *new_block =
          arena_allocate_block(arena, aligned_size);
      if (!new_block) return NULL;

      new_block->next = current;

      if (bebop_atomic_compare_exchange_weak(&arena->current_block, &current,
                                             new_block)) {
        current = new_block;
        bebop_atomic_fetch_add(
            &arena->total_allocated,
            sizeof(bebop_arena_block_t) + new_block->capacity);
      } else {
        arena->free_func(new_block);
        continue;
      }
    }

    size_t old_used = bebop_atomic_load(&current->used);
    if (old_used + aligned_size <= current->capacity) {
      if (bebop_atomic_compare_exchange_weak(&current->used, &old_used,
                                             old_used + aligned_size)) {
        bebop_atomic_fetch_add(&arena->total_used, aligned_size);
        return (uint8_t *)(current + 1) + old_used;
      }
    } else {
      continue;
    }
  }
}

char *bebop_arena_strdup(bebop_arena_t *arena, const char *str, size_t len) {
  if (!arena || !str) return NULL;

  char *copy = (char *)bebop_arena_alloc(arena, len + 1);
  if (copy) {
    memcpy(copy, str, len);
    copy[len] = '\0';
  }
  return copy;
}

// Context management
bebop_context_t *bebop_context_create(void) {
  bebop_context_options_t options = bebop_context_default_options();
  return bebop_context_create_with_options(&options);
}

bebop_context_t *bebop_context_create_with_options(
    const bebop_context_options_t *options) {
  if (!options) return NULL;

  bebop_malloc_func_t malloc_func =
      options->arena_options.allocator.malloc_func
          ? options->arena_options.allocator.malloc_func
          : malloc;

  bebop_context_t *context =
      (bebop_context_t *)malloc_func(sizeof(bebop_context_t));
  if (!context) return NULL;

  context->arena = bebop_arena_create_with_options(&options->arena_options);
  if (!context->arena) {
    bebop_free_func_t free_func =
        options->arena_options.allocator.free_func
            ? options->arena_options.allocator.free_func
            : free;
    free_func(context);
    return NULL;
  }

  context->options = *options;
  return context;
}

void bebop_context_destroy(bebop_context_t *context) {
  if (!context) return;

  bebop_free_func_t free_func = context->arena->free_func;
  bebop_arena_destroy(context->arena);
  free_func(context);
}

void bebop_context_reset(bebop_context_t *context) {
  if (!context) return;
  bebop_arena_reset(context->arena);
}

size_t bebop_context_space_allocated(const bebop_context_t *context) {
  return context ? bebop_atomic_load(&context->arena->total_allocated) : 0;
}

size_t bebop_context_space_used(const bebop_context_t *context) {
  return context ? bebop_atomic_load(&context->arena->total_used) : 0;
}

// Reader functions
bebop_result_t bebop_context_get_reader(bebop_context_t *context,
                                        const uint8_t *buffer,
                                        size_t buffer_length,
                                        bebop_reader_t *reader) {
  if (!context || !buffer || !reader) return BEBOP_ERROR_NULL_POINTER;

  reader->start = buffer;
  reader->current = buffer;
  reader->end = buffer + buffer_length;
  reader->context = context;
  return BEBOP_OK;
}

void bebop_reader_seek(bebop_reader_t *reader, const uint8_t *position) {
  if (!reader) return;
  if (position >= reader->start && position <= reader->end) {
    reader->current = position;
  }
}

void bebop_reader_skip(bebop_reader_t *reader, size_t amount) {
  if (!reader) return;
  if (reader->current + amount <= reader->end) {
    reader->current += amount;
  }
}

// Reader primitive functions
bebop_result_t bebop_reader_read_byte(bebop_reader_t *reader, uint8_t *out) {
  if (BEBOP_UNLIKELY(!reader || !out)) return BEBOP_ERROR_NULL_POINTER;
  if (BEBOP_UNLIKELY(reader->current + sizeof(uint8_t) > reader->end))
    return BEBOP_ERROR_MALFORMED_PACKET;

  *out = *reader->current++;
  return BEBOP_OK;
}

bebop_result_t bebop_reader_read_uint16(bebop_reader_t *reader, uint16_t *out) {
  if (BEBOP_UNLIKELY(!reader || !out)) return BEBOP_ERROR_NULL_POINTER;
  if (BEBOP_UNLIKELY(reader->current + sizeof(uint16_t) > reader->end))
    return BEBOP_ERROR_MALFORMED_PACKET;

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(out, reader->current, sizeof(uint16_t));
  reader->current += sizeof(uint16_t);
#else
  const uint16_t b0 = *reader->current++;
  const uint16_t b1 = *reader->current++;
  *out = (b1 << 8) | b0;
#endif
  return BEBOP_OK;
}

bebop_result_t bebop_reader_read_uint32(bebop_reader_t *reader, uint32_t *out) {
  if (BEBOP_UNLIKELY(!reader || !out)) return BEBOP_ERROR_NULL_POINTER;
  if (BEBOP_UNLIKELY(reader->current + sizeof(uint32_t) > reader->end))
    return BEBOP_ERROR_MALFORMED_PACKET;

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(out, reader->current, sizeof(uint32_t));
  reader->current += sizeof(uint32_t);
#else
  const uint32_t b0 = *reader->current++;
  const uint32_t b1 = *reader->current++;
  const uint32_t b2 = *reader->current++;
  const uint32_t b3 = *reader->current++;
  *out = (b3 << 24) | (b2 << 16) | (b1 << 8) | b0;
#endif
  return BEBOP_OK;
}

bebop_result_t bebop_reader_read_uint64(bebop_reader_t *reader, uint64_t *out) {
  if (BEBOP_UNLIKELY(!reader || !out)) return BEBOP_ERROR_NULL_POINTER;
  if (BEBOP_UNLIKELY(reader->current + sizeof(uint64_t) > reader->end))
    return BEBOP_ERROR_MALFORMED_PACKET;

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(out, reader->current, sizeof(uint64_t));
  reader->current += sizeof(uint64_t);
#else
  const uint64_t b0 = *reader->current++;
  const uint64_t b1 = *reader->current++;
  const uint64_t b2 = *reader->current++;
  const uint64_t b3 = *reader->current++;
  const uint64_t b4 = *reader->current++;
  const uint64_t b5 = *reader->current++;
  const uint64_t b6 = *reader->current++;
  const uint64_t b7 = *reader->current++;
  *out = (b7 << 0x38) | (b6 << 0x30) | (b5 << 0x28) | (b4 << 0x20) |
         (b3 << 0x18) | (b2 << 0x10) | (b1 << 0x08) | b0;
#endif
  return BEBOP_OK;
}

bebop_result_t bebop_reader_read_int16(bebop_reader_t *reader, int16_t *out) {
  return bebop_reader_read_uint16(reader, (uint16_t *)out);
}

bebop_result_t bebop_reader_read_int32(bebop_reader_t *reader, int32_t *out) {
  return bebop_reader_read_uint32(reader, (uint32_t *)out);
}

bebop_result_t bebop_reader_read_int64(bebop_reader_t *reader, int64_t *out) {
  return bebop_reader_read_uint64(reader, (uint64_t *)out);
}

bebop_result_t bebop_reader_read_bool(bebop_reader_t *reader, bool *out) {
  if (BEBOP_UNLIKELY(!reader || !out)) return BEBOP_ERROR_NULL_POINTER;

  uint8_t byte;
  bebop_result_t result = bebop_reader_read_byte(reader, &byte);
  if (BEBOP_LIKELY(result == BEBOP_OK)) {
    *out = byte != 0;
  }
  return result;
}

bebop_result_t bebop_reader_read_float32(bebop_reader_t *reader, float *out) {
  if (!reader || !out) return BEBOP_ERROR_NULL_POINTER;

  uint32_t bits;
  bebop_result_t result = bebop_reader_read_uint32(reader, &bits);
  if (BEBOP_LIKELY(result == BEBOP_OK)) {
    memcpy(out, &bits, sizeof(float));
  }
  return result;
}

bebop_result_t bebop_reader_read_float64(bebop_reader_t *reader, double *out) {
  if (!reader || !out) return BEBOP_ERROR_NULL_POINTER;

  uint64_t bits;
  bebop_result_t result = bebop_reader_read_uint64(reader, &bits);
  if (BEBOP_LIKELY(result == BEBOP_OK)) {
    memcpy(out, &bits, sizeof(double));
  }
  return result;
}

bebop_result_t bebop_reader_read_guid(bebop_reader_t *reader,
                                      bebop_guid_t *out) {
  if (BEBOP_UNLIKELY(!reader || !out)) return BEBOP_ERROR_NULL_POINTER;
  if (BEBOP_UNLIKELY(reader->current + sizeof(bebop_guid_t) > reader->end))
    return BEBOP_ERROR_MALFORMED_PACKET;

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(&out->data1, reader->current + 0, sizeof(uint32_t));
  memcpy(&out->data2, reader->current + 4, sizeof(uint16_t));
  memcpy(&out->data3, reader->current + 6, sizeof(uint16_t));
  memcpy(out->data4, reader->current + 8, 8);
  reader->current += sizeof(bebop_guid_t);
#else
  out->data1 = reader->current[0] | ((uint32_t)(reader->current[1]) << 8) |
               ((uint32_t)(reader->current[2]) << 16) |
               ((uint32_t)(reader->current[3]) << 24);
  out->data2 = reader->current[4] | ((uint16_t)(reader->current[5]) << 8);
  out->data3 = reader->current[6] | ((uint16_t)(reader->current[7]) << 8);
  memcpy(out->data4, reader->current + 8, 8);
  reader->current += sizeof(bebop_guid_t);
#endif
  return BEBOP_OK;
}

bebop_result_t bebop_reader_read_date(bebop_reader_t *reader,
                                      bebop_date_t *out) {
  uint64_t ticks;
  bebop_result_t result = bebop_reader_read_uint64(reader, &ticks);
  if (BEBOP_LIKELY(result == BEBOP_OK)) {
    ticks = ticks & 0x3fffffffffffffffULL;
    *out = (bebop_date_t)(ticks - BEBOP_TICKS_BETWEEN_EPOCHS);
  }
  return result;
}

bebop_result_t bebop_reader_read_length_prefix(bebop_reader_t *reader,
                                               uint32_t *out) {
  bebop_result_t result = bebop_reader_read_uint32(reader, out);
  if (BEBOP_LIKELY(result == BEBOP_OK)) {
    if (BEBOP_UNLIKELY(reader->current + *out > reader->end)) {
      return BEBOP_ERROR_MALFORMED_PACKET;
    }
  }
  return result;
}

bebop_result_t bebop_reader_read_string_view(bebop_reader_t *reader,
                                             bebop_string_view_t *out) {
  if (!reader || !out) return BEBOP_ERROR_NULL_POINTER;

  uint32_t length;
  bebop_result_t result = bebop_reader_read_length_prefix(reader, &length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  out->data = (const char *)reader->current;
  out->length = length;
  reader->current += length;
  return BEBOP_OK;
}

bebop_result_t bebop_reader_read_byte_array_view(bebop_reader_t *reader,
                                                 bebop_byte_array_view_t *out) {
  if (!reader || !out) return BEBOP_ERROR_NULL_POINTER;

  uint32_t length;
  bebop_result_t result = bebop_reader_read_length_prefix(reader, &length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  out->data = reader->current;
  out->length = length;
  reader->current += length;
  return BEBOP_OK;
}

bebop_result_t bebop_reader_read_string_copy(bebop_reader_t *reader,
                                             char **out) {
  if (!reader || !out) return BEBOP_ERROR_NULL_POINTER;

  bebop_string_view_t view;
  bebop_result_t result = bebop_reader_read_string_view(reader, &view);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  *out = bebop_arena_strdup(reader->context->arena, view.data, view.length);
  return *out ? BEBOP_OK : BEBOP_ERROR_OUT_OF_MEMORY;
}

// Writer functions
bebop_result_t bebop_context_get_writer(bebop_context_t *context,
                                        bebop_writer_t *writer) {
  if (!context || !writer) return BEBOP_ERROR_NULL_POINTER;

  uint8_t *buffer = (uint8_t *)bebop_arena_alloc(
      context->arena, context->options.initial_writer_size);
  if (!buffer) return BEBOP_ERROR_OUT_OF_MEMORY;

  writer->buffer = buffer;
  writer->current = buffer;
  writer->end = buffer + context->options.initial_writer_size;
  writer->context = context;
  return BEBOP_OK;
}

bebop_result_t bebop_context_get_writer_with_hint(bebop_context_t *context,
                                                  size_t size_hint,
                                                  bebop_writer_t *writer) {
  if (!context || !writer) return BEBOP_ERROR_NULL_POINTER;

  size_t buffer_size = size_hint > context->options.initial_writer_size
                           ? size_hint
                           : context->options.initial_writer_size;

  uint8_t *buffer = (uint8_t *)bebop_arena_alloc(context->arena, buffer_size);
  if (!buffer) return BEBOP_ERROR_OUT_OF_MEMORY;

  writer->buffer = buffer;
  writer->current = buffer;
  writer->end = buffer + buffer_size;
  writer->context = context;
  return BEBOP_OK;
}

bebop_result_t bebop_writer_ensure_capacity(bebop_writer_t *writer,
                                            size_t additional_bytes) {
  if (!writer) return BEBOP_ERROR_NULL_POINTER;

  if (BEBOP_LIKELY(writer->current + additional_bytes <= writer->end)) {
    return BEBOP_OK;
  }

  size_t current_size = writer->end - writer->buffer;
  size_t used_size = writer->current - writer->buffer;
  size_t new_size = current_size * 2;

  while (new_size < used_size + additional_bytes) {
    new_size *= 2;
  }

  uint8_t *new_buffer =
      (uint8_t *)bebop_arena_alloc(writer->context->arena, new_size);
  if (!new_buffer) return BEBOP_ERROR_OUT_OF_MEMORY;

  memcpy(new_buffer, writer->buffer, used_size);
  writer->buffer = new_buffer;
  writer->current = new_buffer + used_size;
  writer->end = new_buffer + new_size;
  return BEBOP_OK;
}

// Writer primitive functions
bebop_result_t bebop_writer_write_byte(bebop_writer_t *writer, uint8_t value) {
  if (BEBOP_UNLIKELY(writer->current + 1 > writer->end)) {
    bebop_result_t result = bebop_writer_ensure_capacity(writer, 1);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

  *writer->current++ = value;
  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_uint16(bebop_writer_t *writer,
                                         uint16_t value) {
  if (BEBOP_UNLIKELY(writer->current + sizeof(uint16_t) > writer->end)) {
    bebop_result_t result =
        bebop_writer_ensure_capacity(writer, sizeof(uint16_t));
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->current, &value, sizeof(uint16_t));
  writer->current += sizeof(uint16_t);
#else
  *writer->current++ = value;
  *writer->current++ = value >> 8;
#endif
  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_uint32(bebop_writer_t *writer,
                                         uint32_t value) {
  if (BEBOP_UNLIKELY(writer->current + sizeof(uint32_t) > writer->end)) {
    bebop_result_t result =
        bebop_writer_ensure_capacity(writer, sizeof(uint32_t));
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->current, &value, sizeof(uint32_t));
  writer->current += sizeof(uint32_t);
#else
  *writer->current++ = value;
  *writer->current++ = value >> 8;
  *writer->current++ = value >> 16;
  *writer->current++ = value >> 24;
#endif
  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_uint64(bebop_writer_t *writer,
                                         uint64_t value) {
  if (BEBOP_UNLIKELY(writer->current + sizeof(uint64_t) > writer->end)) {
    bebop_result_t result =
        bebop_writer_ensure_capacity(writer, sizeof(uint64_t));
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->current, &value, sizeof(uint64_t));
  writer->current += sizeof(uint64_t);
#else
  *writer->current++ = value;
  *writer->current++ = value >> 0x08;
  *writer->current++ = value >> 0x10;
  *writer->current++ = value >> 0x18;
  *writer->current++ = value >> 0x20;
  *writer->current++ = value >> 0x28;
  *writer->current++ = value >> 0x30;
  *writer->current++ = value >> 0x38;
#endif
  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_int16(bebop_writer_t *writer, int16_t value) {
  return bebop_writer_write_uint16(writer, (uint16_t)value);
}

bebop_result_t bebop_writer_write_int32(bebop_writer_t *writer, int32_t value) {
  return bebop_writer_write_uint32(writer, (uint32_t)value);
}

bebop_result_t bebop_writer_write_int64(bebop_writer_t *writer, int64_t value) {
  return bebop_writer_write_uint64(writer, (uint64_t)value);
}

bebop_result_t bebop_writer_write_bool(bebop_writer_t *writer, bool value) {
  return bebop_writer_write_byte(writer, value ? 1 : 0);
}

bebop_result_t bebop_writer_write_float32(bebop_writer_t *writer, float value) {
  uint32_t bits;
  memcpy(&bits, &value, sizeof(float));
  return bebop_writer_write_uint32(writer, bits);
}

bebop_result_t bebop_writer_write_float64(bebop_writer_t *writer,
                                          double value) {
  uint64_t bits;
  memcpy(&bits, &value, sizeof(double));
  return bebop_writer_write_uint64(writer, bits);
}

bebop_result_t bebop_writer_write_guid(bebop_writer_t *writer,
                                       bebop_guid_t value) {
  if (BEBOP_UNLIKELY(writer->current + sizeof(bebop_guid_t) > writer->end)) {
    bebop_result_t result =
        bebop_writer_ensure_capacity(writer, sizeof(bebop_guid_t));
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->current, &value, sizeof(bebop_guid_t));
  writer->current += sizeof(bebop_guid_t);
#else
  *writer->current++ = value.data1;
  *writer->current++ = value.data1 >> 8;
  *writer->current++ = value.data1 >> 16;
  *writer->current++ = value.data1 >> 24;

  *writer->current++ = value.data2;
  *writer->current++ = value.data2 >> 8;

  *writer->current++ = value.data3;
  *writer->current++ = value.data3 >> 8;

  memcpy(writer->current, value.data4, 8);
  writer->current += 8;
#endif
  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_date(bebop_writer_t *writer,
                                       bebop_date_t value) {
  uint64_t ticks =
      (uint64_t)(value + BEBOP_TICKS_BETWEEN_EPOCHS) & 0x3fffffffffffffffULL;
  return bebop_writer_write_uint64(writer, ticks);
}

bebop_result_t bebop_writer_write_string(bebop_writer_t *writer,
                                         const char *data, size_t length) {
  if (!writer || !data) return BEBOP_ERROR_NULL_POINTER;

  bebop_result_t result = bebop_writer_write_uint32(writer, (uint32_t)length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  if (length > 0) {
    if (BEBOP_UNLIKELY(writer->current + length > writer->end)) {
      result = bebop_writer_ensure_capacity(writer, length);
      if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
    }

    memcpy(writer->current, data, length);
    writer->current += length;
  }

  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_string_view(bebop_writer_t *writer,
                                              bebop_string_view_t view) {
  return bebop_writer_write_string(writer, view.data, view.length);
}

bebop_result_t bebop_writer_write_byte_array(bebop_writer_t *writer,
                                             const uint8_t *data,
                                             size_t length) {
  if (!writer || !data) return BEBOP_ERROR_NULL_POINTER;

  bebop_result_t result = bebop_writer_write_uint32(writer, (uint32_t)length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  if (length > 0) {
    if (BEBOP_UNLIKELY(writer->current + length > writer->end)) {
      result = bebop_writer_ensure_capacity(writer, length);
      if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
    }

    memcpy(writer->current, data, length);
    writer->current += length;
  }

  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_byte_array_view(
    bebop_writer_t *writer, bebop_byte_array_view_t view) {
  return bebop_writer_write_byte_array(writer, view.data, view.length);
}

// Bulk array writers
bebop_result_t bebop_writer_write_float32_array(bebop_writer_t *writer,
                                                const float *data,
                                                size_t length) {
  if (BEBOP_UNLIKELY(!writer || !data)) return BEBOP_ERROR_NULL_POINTER;

  bebop_result_t result = bebop_writer_write_uint32(writer, (uint32_t)length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  if (length == 0) return BEBOP_OK;

  size_t total_bytes = length * sizeof(float);

  if (BEBOP_UNLIKELY(writer->current + total_bytes > writer->end)) {
    result = bebop_writer_ensure_capacity(writer, total_bytes);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->current, data, total_bytes);
  writer->current += total_bytes;
#else
  for (size_t i = 0; i < length; i++) {
    result = bebop_writer_write_float32(writer, data[i]);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }
#endif

  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_float64_array(bebop_writer_t *writer,
                                                const double *data,
                                                size_t length) {
  if (BEBOP_UNLIKELY(!writer || !data)) return BEBOP_ERROR_NULL_POINTER;

  bebop_result_t result = bebop_writer_write_uint32(writer, (uint32_t)length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  if (length == 0) return BEBOP_OK;

  size_t total_bytes = length * sizeof(double);

  if (BEBOP_UNLIKELY(writer->current + total_bytes > writer->end)) {
    result = bebop_writer_ensure_capacity(writer, total_bytes);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->current, data, total_bytes);
  writer->current += total_bytes;
#else
  for (size_t i = 0; i < length; i++) {
    result = bebop_writer_write_float64(writer, data[i]);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }
#endif

  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_uint16_array(bebop_writer_t *writer,
                                               const uint16_t *data,
                                               size_t length) {
  if (BEBOP_UNLIKELY(!writer || !data)) return BEBOP_ERROR_NULL_POINTER;

  bebop_result_t result = bebop_writer_write_uint32(writer, (uint32_t)length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  if (length == 0) return BEBOP_OK;

  size_t total_bytes = length * sizeof(uint16_t);

  if (BEBOP_UNLIKELY(writer->current + total_bytes > writer->end)) {
    result = bebop_writer_ensure_capacity(writer, total_bytes);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->current, data, total_bytes);
  writer->current += total_bytes;
#else
  for (size_t i = 0; i < length; i++) {
    result = bebop_writer_write_uint16(writer, data[i]);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }
#endif

  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_int16_array(bebop_writer_t *writer,
                                              const int16_t *data,
                                              size_t length) {
  return bebop_writer_write_uint16_array(writer, (const uint16_t *)data,
                                         length);
}

bebop_result_t bebop_writer_write_uint32_array(bebop_writer_t *writer,
                                               const uint32_t *data,
                                               size_t length) {
  if (BEBOP_UNLIKELY(!writer || !data)) return BEBOP_ERROR_NULL_POINTER;

  bebop_result_t result = bebop_writer_write_uint32(writer, (uint32_t)length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  if (length == 0) return BEBOP_OK;

  size_t total_bytes = length * sizeof(uint32_t);

  if (BEBOP_UNLIKELY(writer->current + total_bytes > writer->end)) {
    result = bebop_writer_ensure_capacity(writer, total_bytes);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->current, data, total_bytes);
  writer->current += total_bytes;
#else
  for (size_t i = 0; i < length; i++) {
    result = bebop_writer_write_uint32(writer, data[i]);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }
#endif

  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_int32_array(bebop_writer_t *writer,
                                              const int32_t *data,
                                              size_t length) {
  return bebop_writer_write_uint32_array(writer, (const uint32_t *)data,
                                         length);
}

bebop_result_t bebop_writer_write_uint64_array(bebop_writer_t *writer,
                                               const uint64_t *data,
                                               size_t length) {
  if (BEBOP_UNLIKELY(!writer || !data)) return BEBOP_ERROR_NULL_POINTER;

  bebop_result_t result = bebop_writer_write_uint32(writer, (uint32_t)length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  if (length == 0) return BEBOP_OK;

  size_t total_bytes = length * sizeof(uint64_t);

  if (BEBOP_UNLIKELY(writer->current + total_bytes > writer->end)) {
    result = bebop_writer_ensure_capacity(writer, total_bytes);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->current, data, total_bytes);
  writer->current += total_bytes;
#else
  for (size_t i = 0; i < length; i++) {
    result = bebop_writer_write_uint64(writer, data[i]);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }
#endif

  return BEBOP_OK;
}

bebop_result_t bebop_writer_write_int64_array(bebop_writer_t *writer,
                                              const int64_t *data,
                                              size_t length) {
  return bebop_writer_write_uint64_array(writer, (const uint64_t *)data,
                                         length);
}

bebop_result_t bebop_writer_write_uint8_array(bebop_writer_t *writer,
                                              const uint8_t *data,
                                              size_t length) {
  return bebop_writer_write_byte_array(writer, data, length);
}

bebop_result_t bebop_writer_write_bool_array(bebop_writer_t *writer,
                                             const bool *data, size_t length) {
  if (BEBOP_UNLIKELY(!writer || !data)) return BEBOP_ERROR_NULL_POINTER;

  bebop_result_t result = bebop_writer_write_uint32(writer, (uint32_t)length);
  if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;

  if (length == 0) return BEBOP_OK;

  if (BEBOP_UNLIKELY(writer->current + length > writer->end)) {
    result = bebop_writer_ensure_capacity(writer, length);
    if (BEBOP_UNLIKELY(result != BEBOP_OK)) return result;
  }

  for (size_t i = 0; i < length; i++) {
    *writer->current++ = data[i] ? 1 : 0;
  }

  return BEBOP_OK;
}

bebop_result_t bebop_writer_reserve_message_length(bebop_writer_t *writer,
                                                   size_t *position) {
  if (!writer || !position) return BEBOP_ERROR_NULL_POINTER;

  *position = bebop_writer_length(writer);
  return bebop_writer_write_uint32(writer, 0);
}

bebop_result_t bebop_writer_fill_message_length(bebop_writer_t *writer,
                                                size_t position,
                                                uint32_t length) {
  if (!writer) return BEBOP_ERROR_NULL_POINTER;
  if (BEBOP_UNLIKELY(position + sizeof(uint32_t) >
                     bebop_writer_length(writer))) {
    return BEBOP_ERROR_MALFORMED_PACKET;
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(writer->buffer + position, &length, sizeof(uint32_t));
#else
  writer->buffer[position++] = length;
  writer->buffer[position++] = length >> 8;
  writer->buffer[position++] = length >> 16;
  writer->buffer[position++] = length >> 24;
#endif
  return BEBOP_OK;
}

bebop_result_t bebop_writer_get_buffer(bebop_writer_t *writer, uint8_t **buffer,
                                       size_t *length) {
  if (!writer || !buffer || !length) return BEBOP_ERROR_NULL_POINTER;

  *buffer = writer->buffer;
  *length = bebop_writer_length(writer);
  return BEBOP_OK;
}

// GUID utility functions
static const uint8_t ascii_to_hex[256] = {
    0, 0, 0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0, 0, 0, 0, 0,
    0, 0, 0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0, 0, 0, 0, 0,
    0, 0, 0,  0,  0,  0,  0,  0,  0,  0,  0,  1,  2,  3,  4, 5, 6, 7, 8,
    9, 0, 0,  0,  0,  0,  0,  0,  10, 11, 12, 13, 14, 15, 0, 0, 0, 0, 0,
    0, 0, 0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0, 0, 0, 0, 0,
    0, 0, 10, 11, 12, 13, 14, 15, 0,  0,  0,  0,  0,  0,  0, 0, 0, 0, 0,
    0, 0, 0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
    /* rest are zeros */
};

static const char hex_chars[16] = {'0', '1', '2', '3', '4', '5', '6', '7',
                                   '8', '9', 'a', 'b', 'c', 'd', 'e', 'f'};

static const int guid_layout[] = {3,  2, 1, 0,  -1, 5,  4,  -1, 7,  6,
                                  -1, 8, 9, -1, 10, 11, 12, 13, 14, 15};

bebop_guid_t bebop_guid_from_string(const char *str) {
  bebop_guid_t guid = {0};
  if (!str || *str == '\0') return guid;

  uint8_t bytes[16] = {0};
  const char *s = str;

  for (size_t i = 0; i < sizeof(guid_layout) / sizeof(guid_layout[0]); i++) {
    int byte_idx = guid_layout[i];
    if (byte_idx == -1) {
      if (*s == '-') s++;
    } else {
      if (*s == '\0' || *(s + 1) == '\0') {
        return guid;
      }

      uint8_t high = (uint8_t)*s++;
      uint8_t low = (uint8_t)*s++;
      bytes[byte_idx] = (ascii_to_hex[high] << 4) | ascii_to_hex[low];
    }
  }

#if BEBOP_ASSUME_LITTLE_ENDIAN
  memcpy(&guid.data1, bytes + 0, sizeof(uint32_t));
  memcpy(&guid.data2, bytes + 4, sizeof(uint16_t));
  memcpy(&guid.data3, bytes + 6, sizeof(uint16_t));
#else
  guid.data1 = bytes[0] | (uint32_t)(bytes[1] << 8) |
               (uint32_t)(bytes[2] << 16) | (uint32_t)(bytes[3] << 24);
  guid.data2 = bytes[4] | (uint16_t)(bytes[5] << 8);
  guid.data3 = bytes[6] | (uint16_t)(bytes[7] << 8);
#endif
  memcpy(guid.data4, bytes + 8, 8);
  return guid;
}

bebop_result_t bebop_guid_to_string(bebop_guid_t guid, bebop_context_t *context,
                                    char **out) {
  if (!context || !out) return BEBOP_ERROR_NULL_POINTER;

  char *str =
      (char *)bebop_arena_alloc(context->arena, BEBOP_GUID_STRING_LENGTH + 1);
  if (!str) return BEBOP_ERROR_OUT_OF_MEMORY;

  char *p = str;

  uint32_t d1 = guid.data1;
  *p++ = hex_chars[(d1 >> 28) & 0xF];
  *p++ = hex_chars[(d1 >> 24) & 0xF];
  *p++ = hex_chars[(d1 >> 20) & 0xF];
  *p++ = hex_chars[(d1 >> 16) & 0xF];
  *p++ = hex_chars[(d1 >> 12) & 0xF];
  *p++ = hex_chars[(d1 >> 8) & 0xF];
  *p++ = hex_chars[(d1 >> 4) & 0xF];
  *p++ = hex_chars[d1 & 0xF];
  *p++ = '-';

  uint16_t d2 = guid.data2;
  *p++ = hex_chars[(d2 >> 12) & 0xF];
  *p++ = hex_chars[(d2 >> 8) & 0xF];
  *p++ = hex_chars[(d2 >> 4) & 0xF];
  *p++ = hex_chars[d2 & 0xF];
  *p++ = '-';

  uint16_t d3 = guid.data3;
  *p++ = hex_chars[(d3 >> 12) & 0xF];
  *p++ = hex_chars[(d3 >> 8) & 0xF];
  *p++ = hex_chars[(d3 >> 4) & 0xF];
  *p++ = hex_chars[d3 & 0xF];
  *p++ = '-';

  *p++ = hex_chars[(guid.data4[0] >> 4) & 0xF];
  *p++ = hex_chars[guid.data4[0] & 0xF];
  *p++ = hex_chars[(guid.data4[1] >> 4) & 0xF];
  *p++ = hex_chars[guid.data4[1] & 0xF];
  *p++ = '-';

  for (int i = 2; i < 8; i++) {
    *p++ = hex_chars[(guid.data4[i] >> 4) & 0xF];
    *p++ = hex_chars[guid.data4[i] & 0xF];
  }

  *p = '\0';
  *out = str;
  return BEBOP_OK;
}

// utility functions
bebop_context_options_t bebop_context_default_options(void) {
  bebop_context_options_t options = {
      .arena_options = {.initial_block_size = 4096,
                        .max_block_size = 1048576,
                        .allocator = {.malloc_func = NULL, .free_func = NULL}},
      .initial_writer_size = 1024};
  return options;
}

size_t bebop_reader_bytes_read(const bebop_reader_t *reader) {
  return reader ? (size_t)(reader->current - reader->start) : 0;
}

const uint8_t *bebop_reader_position(const bebop_reader_t *reader) {
  return reader ? reader->current : NULL;
}

size_t bebop_writer_length(const bebop_writer_t *writer) {
  return writer ? (size_t)(writer->current - writer->buffer) : 0;
}

size_t bebop_writer_remaining(const bebop_writer_t *writer) {
  return writer ? (size_t)(writer->end - writer->current) : 0;
}

bebop_string_view_t bebop_string_view_from_cstr(const char *str) {
  bebop_string_view_t view = {str, str ? strlen(str) : 0};
  return view;
}

bool bebop_string_view_equal(bebop_string_view_t a, bebop_string_view_t b) {
  return a.length == b.length &&
         (a.length == 0 || memcmp(a.data, b.data, a.length) == 0);
}

bool bebop_guid_equal(bebop_guid_t a, bebop_guid_t b) {
  return memcmp(&a, &b, sizeof(bebop_guid_t)) == 0;
}

void *bebop_context_alloc(bebop_context_t *context, size_t size) {
  return context ? bebop_arena_alloc(context->arena, size) : NULL;
}

char *bebop_context_strdup(bebop_context_t *context, const char *str,
                           size_t len) {
  return context ? bebop_arena_strdup(context->arena, str, len) : NULL;
}