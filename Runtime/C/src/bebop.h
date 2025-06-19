/**
 * @file bebop.h
 * @brief Bebop C Runtime - High-performance binary (de)serialization library
 *
 */

#ifndef BEBOP_H
#define BEBOP_H

#include <assert.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#ifdef __cplusplus
extern "C" {
#endif

/** @defgroup version Version and Build Configuration
 *  @{
 */

#ifndef BEBOPC_VER_MAJOR
#define BEBOPC_VER_MAJOR 0 /**< Major version number */
#endif

#ifndef BEBOPC_VER_MINOR
#define BEBOPC_VER_MINOR 0 /**< Minor version number */
#endif

#ifndef BEBOPC_VER_PATCH
#define BEBOPC_VER_PATCH 0 /**< Patch version number */
#endif

#ifndef BEBOPC_VER_INFO
#define BEBOPC_VER_INFO "0"
#endif

/** Combined version as 32-bit integer */
#define BEBOPC_VER                                 \
  ((uint32_t)(((uint8_t)BEBOPC_VER_MAJOR << 24u) | \
              ((uint8_t)BEBOPC_VER_MINOR << 16u) | \
              ((uint8_t)BEBOPC_VER_PATCH << 8u) | (uint8_t)0))

#ifndef BEBOP_ASSUME_LITTLE_ENDIAN
#define BEBOP_ASSUME_LITTLE_ENDIAN \
  1 /**< Runtime assumes little-endian byte order */
#endif

/** Branch prediction hints */
#ifndef BEBOP_LIKELY
#if defined(__GNUC__) || defined(__clang__)
#define BEBOP_LIKELY(x) __builtin_expect(!!(x), 1)
#define BEBOP_UNLIKELY(x) __builtin_expect(!!(x), 0)
#else
#define BEBOP_LIKELY(x) (x)
#define BEBOP_UNLIKELY(x) (x)
#endif
#endif

/** Single-threaded optimization */
#ifdef BEBOP_SINGLE_THREADED
#define bebop_atomic_load(ptr) (*(ptr))
#define bebop_atomic_store(ptr, val) (*(ptr) = (val))
#define bebop_atomic_fetch_add(ptr, val) ((*(ptr)) += (val))
#define bebop_atomic_compare_exchange_weak(ptr, expected, desired) \
  (*(ptr) == *(expected) ? (*(ptr) = (desired), true)              \
                         : (*(expected) = *(ptr), false))
#define bebop_atomic_init(ptr, val) (*(ptr) = (val))
#else
#include <stdatomic.h>
#define bebop_atomic_load(ptr) atomic_load(ptr)
#define bebop_atomic_store(ptr, val) atomic_store(ptr, val)
#define bebop_atomic_fetch_add(ptr, val) atomic_fetch_add(ptr, val)
#define bebop_atomic_compare_exchange_weak(ptr, expected, desired) \
  atomic_compare_exchange_weak(ptr, expected, desired)
#define bebop_atomic_init(ptr, val) atomic_init(ptr, val)
#endif

/** @defgroup datetime Date and Time Constants
 *  @{
 */
#define BEBOP_TICKS_PER_SECOND 10000000LL /**< 100ns ticks per second */
#define BEBOP_TICKS_BETWEEN_EPOCHS \
  621355968000000000LL /**< Ticks between Unix and .NET epochs */
/** @} */

/** @} */

/** @defgroup errors Error Handling
 *  @{
 */

/** Operation result codes */
typedef enum {
  BEBOP_OK = 0,                     /**< Operation successful */
  BEBOP_ERROR_MALFORMED_PACKET = 1, /**< Invalid or corrupted data */
  BEBOP_ERROR_BUFFER_TOO_SMALL = 2, /**< Buffer capacity exceeded */
  BEBOP_ERROR_OUT_OF_MEMORY = 3,    /**< Memory allocation failed */
  BEBOP_ERROR_NULL_POINTER = 4,     /**< Null pointer argument */
  BEBOP_ERROR_INVALID_CONTEXT = 5   /**< Context in invalid state */
} bebop_result_t;

/** @} */

/** @defgroup types Core Data Types
 *  @{
 */

/*------------------------------------------------------------
 * BEBOP_DEPRECATED / BEBOP_DEPRECATED_MSG
 *
 * Marks functions, types or globals as deprecated in both
 * C and C++. Uses the standard [[deprecated]] when available
 * (C23 or C++14), otherwise falls back to compiler-specific
 * attributes, and finally to a no-op with a compile-time warning.
 *------------------------------------------------------------*/
// clang-format off
  #ifndef BEBOP_DEPRECATED
  
  /* 1) C23 with [[deprecated]] support */
  #if defined(__STDC_VERSION__) && __STDC_VERSION__ >= 202311L
    #ifdef __has_c_attribute
      #if __has_c_attribute(deprecated)
        #define BEBOP_DEPRECATED [[deprecated]]
        #define BEBOP_DEPRECATED_MSG(msg) [[deprecated(msg)]]
      #endif
    #endif
  #endif
  
  /* Only define if not already defined above */
  #ifndef BEBOP_DEPRECATED
    /* 2) C++14 with [[deprecated]] support */
    #if defined(__cplusplus) && __cplusplus >= 201402L && \
        defined(__has_cpp_attribute) && __has_cpp_attribute(deprecated)
      #define BEBOP_DEPRECATED [[deprecated]]
      #define BEBOP_DEPRECATED_MSG(msg) [[deprecated(msg)]]
  
    /* 3) GCC / Clang */
    #elif defined(__GNUC__) || defined(__clang__)
      #define BEBOP_DEPRECATED __attribute__((deprecated))
      #define BEBOP_DEPRECATED_MSG(msg) __attribute__((deprecated(msg)))
  
    /* 4) MSVC */
    #elif defined(_MSC_VER)
      #define BEBOP_DEPRECATED __declspec(deprecated)
      #define BEBOP_DEPRECATED_MSG(msg) __declspec(deprecated(msg))
  
    /* 5) Unknown compiler */
    #else
      #if defined(_MSC_VER)
        #pragma message("WARNING: BEBOP_DEPRECATED is not supported on this compiler")
      #elif defined(__GNUC__) || defined(__clang__)
        #warning "BEBOP_DEPRECATED is not supported on this compiler"
      #else
        #pragma message("WARNING: BEBOP_DEPRECATED is not supported on this compiler")
      #endif
      #define BEBOP_DEPRECATED
      #define BEBOP_DEPRECATED_MSG(msg)
    #endif
  #endif
  
  #endif
// clang-format on

/** Globally unique identifier (RFC 4122 compatible) */
#pragma pack(push, 1)
typedef struct {
  uint32_t data1;   /**< First 32 bits */
  uint16_t data2;   /**< Next 16 bits */
  uint16_t data3;   /**< Next 16 bits */
  uint8_t data4[8]; /**< Final 64 bits */
} bebop_guid_t;
#pragma pack(pop)

typedef int64_t bebop_date_t; /**< Date as 100ns ticks since Unix epoch */

/** Zero-copy string view */
typedef struct {
  const char *data; /**< Pointer to string data (not null-terminated) */
  size_t length;    /**< String length in bytes */
} bebop_string_view_t;

/** Zero-copy byte array view */
typedef struct {
  const uint8_t *data; /**< Pointer to byte data */
  size_t length;       /**< Array length in bytes */
} bebop_byte_array_view_t;

/** @defgroup arrayviews Typed Array Views
 *  @{
 */

/** Generic array view declaration macro */
#define BEBOP_DECLARE_ARRAY_VIEW(type_name, element_type) \
  typedef struct {                                        \
    const element_type *data;                             \
    size_t length;                                        \
  } bebop_##type_name##_array_view_t

/** Allocated array declaration macro */
#define BEBOP_DECLARE_ARRAY_ALLOC(type_name, element_type) \
  typedef struct {                                         \
    element_type *data;                                    \
    size_t length;                                         \
  } bebop_##type_name##_array_t

BEBOP_DECLARE_ARRAY_VIEW(uint8,
                         uint8_t); /**< Unsigned 8-bit integer array view */
BEBOP_DECLARE_ARRAY_VIEW(uint16,
                         uint16_t); /**< Unsigned 16-bit integer array view */
BEBOP_DECLARE_ARRAY_VIEW(uint32,
                         uint32_t); /**< Unsigned 32-bit integer array view */
BEBOP_DECLARE_ARRAY_VIEW(uint64,
                         uint64_t); /**< Unsigned 64-bit integer array view */
BEBOP_DECLARE_ARRAY_VIEW(int16,
                         int16_t); /**< Signed 16-bit integer array view */
BEBOP_DECLARE_ARRAY_VIEW(int32,
                         int32_t); /**< Signed 32-bit integer array view */
BEBOP_DECLARE_ARRAY_VIEW(int64,
                         int64_t); /**< Signed 64-bit integer array view */
BEBOP_DECLARE_ARRAY_VIEW(float32,
                         float); /**< 32-bit floating point array view */
BEBOP_DECLARE_ARRAY_VIEW(float64,
                         double);     /**< 64-bit floating point array view */
BEBOP_DECLARE_ARRAY_VIEW(bool, bool); /**< Boolean array view */
BEBOP_DECLARE_ARRAY_VIEW(guid, bebop_guid_t); /**< GUID array view */
BEBOP_DECLARE_ARRAY_VIEW(date, bebop_date_t); /**< Date array view */

BEBOP_DECLARE_ARRAY_ALLOC(uint8, uint8_t); /**< Unsigned 8-bit integer array */
BEBOP_DECLARE_ARRAY_ALLOC(uint16,
                          uint16_t); /**< Unsigned 16-bit integer array */
BEBOP_DECLARE_ARRAY_ALLOC(uint32,
                          uint32_t); /**< Unsigned 32-bit integer array */
BEBOP_DECLARE_ARRAY_ALLOC(uint64,
                          uint64_t);       /**< Unsigned 64-bit integer array */
BEBOP_DECLARE_ARRAY_ALLOC(int16, int16_t); /**< Signed 16-bit integer array */
BEBOP_DECLARE_ARRAY_ALLOC(int32, int32_t); /**< Signed 32-bit integer array */
BEBOP_DECLARE_ARRAY_ALLOC(int64, int64_t); /**< Signed 64-bit integer array */
BEBOP_DECLARE_ARRAY_ALLOC(float32, float); /**< 32-bit floating point array */
BEBOP_DECLARE_ARRAY_ALLOC(float64, double); /**< 64-bit floating point array */
BEBOP_DECLARE_ARRAY_ALLOC(bool, bool);      /**< Boolean array */
BEBOP_DECLARE_ARRAY_ALLOC(string_view,
                          bebop_string_view_t); /**< String view array */
BEBOP_DECLARE_ARRAY_ALLOC(guid, bebop_guid_t);  /**< GUID array */
BEBOP_DECLARE_ARRAY_ALLOC(date, bebop_date_t);  /**< Date array */

#define BEBOP_DECLARE_MAP_VIEW(key_name, key_type, value_name, value_type) \
  typedef struct {                                                         \
    key_type key;                                                          \
    value_type value;                                                      \
  } bebop_##key_name##_##value_name##_map_entry_t;                         \
  typedef struct {                                                         \
    const bebop_##key_name##_##value_name##_map_entry_t *entries;          \
    size_t length;                                                         \
  } bebop_##key_name##_##value_name##_map_view_t

#define BEBOP_DECLARE_MAP_ALLOC(key_name, key_type, value_name, value_type) \
  typedef struct {                                                          \
    key_type key;                                                           \
    value_type value;                                                       \
  } bebop_##key_name##_##value_name##_map_entry_t;                          \
  typedef struct {                                                          \
    bebop_##key_name##_##value_name##_map_entry_t *entries;                 \
    size_t length;                                                          \
  } bebop_##key_name##_##value_name##_map_t

/** @} */
/** @} */

/** @defgroup optional Optional Type System
 *  @{
 */

/** Simple optional wrapper */
#define bebop_optional(T) \
  struct {                \
    bool has_value;       \
    T value;              \
  }

/** Create empty optional */
#define bebop_none() {.has_value = false}

/** Create optional with value */
#define bebop_some(val) {.has_value = true, .value = (val)}

/** Check if optional has value */
#define bebop_is_some(optional) ((optional).has_value)
#define bebop_is_none(optional) (!(optional).has_value)

/** Get value (unsafe - check has_value first) */
#define bebop_unwrap(optional) ((optional).value)

/** Get value with default */
#define bebop_unwrap_or(optional, default_val) \
  ((optional).has_value ? (optional).value : (default_val))

/** Assign optional value */
#define bebop_assign_some(optional_field, val) \
  do {                                         \
    (optional_field).has_value = true;         \
    (optional_field).value = (val);            \
  } while (0)

/** Assign optional to none */
#define bebop_assign_none(optional_field) \
  do {                                    \
    (optional_field).has_value = false;   \
  } while (0)

/** Serialize optional (writes bool + value if present) */
#define bebop_write_optional(writer, optional, write_func)     \
  do {                                                         \
    bebop_result_t _res =                                      \
        bebop_writer_write_bool(writer, (optional).has_value); \
    if (_res != BEBOP_OK) return _res;                         \
    if ((optional).has_value) {                                \
      _res = write_func(writer, (optional).value);             \
      if (_res != BEBOP_OK) return _res;                       \
    }                                                          \
  } while (0)

/** Deserialize optional */
#define bebop_read_optional(reader, optional_ptr, read_func)        \
  do {                                                              \
    bebop_result_t _res =                                           \
        bebop_reader_read_bool(reader, &(optional_ptr)->has_value); \
    if (_res != BEBOP_OK) return _res;                              \
    if ((optional_ptr)->has_value) {                                \
      _res = read_func(reader, &(optional_ptr)->value);             \
      if (_res != BEBOP_OK) return _res;                            \
    }                                                               \
  } while (0)

/** @} */

/** @defgroup memory Custom Memory Management
 *  @{
 */

/** Custom allocator function */
typedef void *(*bebop_malloc_func_t)(size_t size);

/** Custom deallocator function */
typedef void (*bebop_free_func_t)(void *ptr);

/** Memory allocator configuration */
typedef struct {
  bebop_malloc_func_t
      malloc_func; /**< Custom malloc function or NULL for default */
  bebop_free_func_t free_func; /**< Custom free function or NULL for default */
} bebop_allocator_t;

/** @} */

/** @defgroup arena Thread-Safe Arena Allocation
 *  @{
 */

/** Arena memory block (internal structure) */
typedef struct bebop_arena_block {
  struct bebop_arena_block *next; /**< Next block in chain */
#ifdef BEBOP_SINGLE_THREADED
  size_t used; /**< Bytes used in this block */
#else
  _Atomic size_t used; /**< Bytes used in this block */
#endif
  size_t capacity; /**< Total block capacity */
} bebop_arena_block_t;

/** Arena configuration options */
typedef struct {
  size_t initial_block_size;   /**< Size of first allocated block */
  size_t max_block_size;       /**< Maximum size for new blocks */
  bebop_allocator_t allocator; /**< Custom allocator functions */
} bebop_arena_options_t;

/** Thread-safe memory arena */
typedef struct {
#ifdef BEBOP_SINGLE_THREADED
  bebop_arena_block_t *current_block; /**< Current allocation block */
  size_t total_allocated;             /**< Total bytes allocated */
  size_t total_used;                  /**< Total bytes in use */
#else
  _Atomic(bebop_arena_block_t *) current_block; /**< Current allocation block */
  _Atomic size_t total_allocated;               /**< Total bytes allocated */
  _Atomic size_t total_used;                    /**< Total bytes in use */
#endif
  bebop_arena_options_t options;   /**< Arena configuration */
  bebop_malloc_func_t malloc_func; /**< Cached malloc function */
  bebop_free_func_t free_func;     /**< Cached free function */
} bebop_arena_t;

/** @} */

/** @defgroup context Bebop Context
 *  @{
 */

/** Bebop context configuration */
typedef struct {
  bebop_arena_options_t arena_options; /**< Arena configuration */
  size_t initial_writer_size;          /**< Initial writer buffer size */
} bebop_context_options_t;

/** Forward declarations */
typedef struct bebop_reader bebop_reader_t;
typedef struct bebop_writer bebop_writer_t;

/** Bebop context - manages arena and provides readers/writers */
typedef struct {
  bebop_arena_t *arena;            /**< Shared arena for all operations */
  bebop_context_options_t options; /**< Context configuration */
} bebop_context_t;

/**
 * @brief Create context with default options
 * @return New context or NULL on failure
 */
bebop_context_t *bebop_context_create(void);

/**
 * @brief Create context with custom options
 * @param options Configuration options
 * @return New context or NULL on failure
 */
bebop_context_t *bebop_context_create_with_options(
    const bebop_context_options_t *options);

/**
 * @brief Destroy context and free all memory
 * @param context Context to destroy
 */
void bebop_context_destroy(bebop_context_t *context);

/**
 * @brief Reset context, keeping allocated blocks for reuse
 * @param context Context to reset
 */
void bebop_context_reset(bebop_context_t *context);

/**
 * @brief Get total allocated memory
 * @param context Target context
 * @return Bytes allocated across all blocks
 */
size_t bebop_context_space_allocated(const bebop_context_t *context);

/**
 * @brief Get total used memory
 * @param context Target context
 * @return Bytes currently in use
 */
size_t bebop_context_space_used(const bebop_context_t *context);

/** @} */

/** @defgroup reader Zero-Copy Deserialization
 *  @{
 */

/** Binary data reader state */
struct bebop_reader {
  const uint8_t *start;     /**< Buffer start */
  const uint8_t *current;   /**< Current read position */
  const uint8_t *end;       /**< Buffer end */
  bebop_context_t *context; /**< Associated context for allocations */
};

/**
 * @brief Get reader from context
 * @param context Source context
 * @param buffer Source data buffer
 * @param buffer_length Buffer size in bytes
 * @param reader Output reader
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_context_get_reader(bebop_context_t *context,
                                        const uint8_t *buffer,
                                        size_t buffer_length,
                                        bebop_reader_t *reader);

/**
 * @brief Seek to specific position
 * @param reader Target reader
 * @param position New position (must be within buffer bounds)
 */
void bebop_reader_seek(bebop_reader_t *reader, const uint8_t *position);

/**
 * @brief Skip bytes forward
 * @param reader Target reader
 * @param amount Bytes to skip
 */
void bebop_reader_skip(bebop_reader_t *reader, size_t amount);

/** @defgroup reader_primitives Primitive Type Reading
 *  @{
 */

bebop_result_t bebop_reader_read_byte(bebop_reader_t *reader, uint8_t *out);

bebop_result_t bebop_reader_read_uint16(bebop_reader_t *reader, uint16_t *out);

bebop_result_t bebop_reader_read_uint32(bebop_reader_t *reader, uint32_t *out);

bebop_result_t bebop_reader_read_uint64(bebop_reader_t *reader, uint64_t *out);

bebop_result_t bebop_reader_read_int16(bebop_reader_t *reader, int16_t *out);

bebop_result_t bebop_reader_read_int32(bebop_reader_t *reader, int32_t *out);

bebop_result_t bebop_reader_read_int64(bebop_reader_t *reader, int64_t *out);

bebop_result_t bebop_reader_read_bool(bebop_reader_t *reader, bool *out);

/** @} */

bebop_result_t bebop_reader_read_float32(bebop_reader_t *reader, float *out);
bebop_result_t bebop_reader_read_float64(bebop_reader_t *reader, double *out);
bebop_result_t bebop_reader_read_guid(bebop_reader_t *reader,
                                      bebop_guid_t *out);
bebop_result_t bebop_reader_read_date(bebop_reader_t *reader,
                                      bebop_date_t *out);

/**
 * @brief Read and validate length prefix
 * @param reader Source reader
 * @param out Length value
 * @return BEBOP_OK or BEBOP_ERROR_MALFORMED_PACKET if length exceeds buffer
 */
bebop_result_t bebop_reader_read_length_prefix(bebop_reader_t *reader,
                                               uint32_t *out);

/**
 * @brief Read string as zero-copy view
 * @param reader Source reader
 * @param out String view (points into original buffer)
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_reader_read_string_view(bebop_reader_t *reader,
                                             bebop_string_view_t *out);

/**
 * @brief Read byte array as zero-copy view
 * @param reader Source reader
 * @param out Byte array view (points into original buffer)
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_reader_read_byte_array_view(bebop_reader_t *reader,
                                                 bebop_byte_array_view_t *out);

/**
 * @brief Read string and copy to arena
 * @param reader Source reader
 * @param out Null-terminated string copy
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_reader_read_string_copy(bebop_reader_t *reader,
                                             char **out);

/** @} */

/** @defgroup writer Arena-Backed Serialization
 *  @{
 */

/** Binary data writer state */
struct bebop_writer {
  uint8_t *buffer;          /**< Output buffer */
  uint8_t *current;         /**< Current write position */
  uint8_t *end;             /**< Buffer end */
  bebop_context_t *context; /**< Associated context for allocations */
};

/**
 * @brief Get writer from context
 * @param context Source context
 * @param writer Output writer
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_context_get_writer(bebop_context_t *context,
                                        bebop_writer_t *writer);

/**
 * @brief Get writer from context with size hint
 * @param context Source context
 * @param size_hint Expected size needed
 * @param writer Output writer
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_context_get_writer_with_hint(bebop_context_t *context,
                                                  size_t size_hint,
                                                  bebop_writer_t *writer);

/**
 * @brief Ensure buffer has space for additional bytes
 * @param writer Target writer
 * @param additional_bytes Required additional capacity
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_ensure_capacity(bebop_writer_t *writer,
                                            size_t additional_bytes);

/** @defgroup writer_primitives Primitive Type Writing
 *  @{
 */

bebop_result_t bebop_writer_write_byte(bebop_writer_t *writer, uint8_t value);

bebop_result_t bebop_writer_write_uint16(bebop_writer_t *writer,
                                         uint16_t value);

bebop_result_t bebop_writer_write_uint32(bebop_writer_t *writer,
                                         uint32_t value);

bebop_result_t bebop_writer_write_uint64(bebop_writer_t *writer,
                                         uint64_t value);

bebop_result_t bebop_writer_write_int16(bebop_writer_t *writer, int16_t value);

bebop_result_t bebop_writer_write_int32(bebop_writer_t *writer, int32_t value);

bebop_result_t bebop_writer_write_int64(bebop_writer_t *writer, int64_t value);

bebop_result_t bebop_writer_write_bool(bebop_writer_t *writer, bool value);

/** @} */

bebop_result_t bebop_writer_write_float32(bebop_writer_t *writer, float value);
bebop_result_t bebop_writer_write_float64(bebop_writer_t *writer, double value);
bebop_result_t bebop_writer_write_guid(bebop_writer_t *writer,
                                       bebop_guid_t value);
bebop_result_t bebop_writer_write_date(bebop_writer_t *writer,
                                       bebop_date_t value);

/**
 * @brief Write string with length prefix
 * @param writer Target writer
 * @param data String data
 * @param length String length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_string(bebop_writer_t *writer,
                                         const char *data, size_t length);

/**
 * @brief Write string view with length prefix
 * @param writer Target writer
 * @param view String view
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_string_view(bebop_writer_t *writer,
                                              bebop_string_view_t view);

/**
 * @brief Write byte array with length prefix
 * @param writer Target writer
 * @param data Byte data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_byte_array(bebop_writer_t *writer,
                                             const uint8_t *data,
                                             size_t length);

/**
 * @brief Write byte array view with length prefix
 * @param writer Target writer
 * @param view Byte array view
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_byte_array_view(bebop_writer_t *writer,
                                                  bebop_byte_array_view_t view);

/** @defgroup bulk_array_writers Bulk Array Writers
 *  @{
 */

/**
 * @brief Write float32 array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_float32_array(bebop_writer_t *writer,
                                                const float *data,
                                                size_t length);

/**
 * @brief Write float64 array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_float64_array(bebop_writer_t *writer,
                                                const double *data,
                                                size_t length);

/**
 * @brief Write uint16 array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_uint16_array(bebop_writer_t *writer,
                                               const uint16_t *data,
                                               size_t length);

/**
 * @brief Write int16 array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_int16_array(bebop_writer_t *writer,
                                              const int16_t *data,
                                              size_t length);

/**
 * @brief Write uint32 array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_uint32_array(bebop_writer_t *writer,
                                               const uint32_t *data,
                                               size_t length);

/**
 * @brief Write int32 array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_int32_array(bebop_writer_t *writer,
                                              const int32_t *data,
                                              size_t length);

/**
 * @brief Write uint64 array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_uint64_array(bebop_writer_t *writer,
                                               const uint64_t *data,
                                               size_t length);

/**
 * @brief Write int64 array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_int64_array(bebop_writer_t *writer,
                                              const int64_t *data,
                                              size_t length);

/**
 * @brief Write uint8 array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_uint8_array(bebop_writer_t *writer,
                                              const uint8_t *data,
                                              size_t length);

/**
 * @brief Write bool array with length prefix (bulk operation)
 * @param writer Target writer
 * @param data Array data
 * @param length Array length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_write_bool_array(bebop_writer_t *writer,
                                             const bool *data, size_t length);

/** @} */

/**
 * @brief Reserve space for message length (writes placeholder)
 * @param writer Target writer
 * @param position Position of length field for later update
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_reserve_message_length(bebop_writer_t *writer,
                                                   size_t *position);

/**
 * @brief Fill previously reserved message length
 * @param writer Target writer
 * @param position Position from bebop_writer_reserve_message_length
 * @param length Actual message length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_fill_message_length(bebop_writer_t *writer,
                                                size_t position,
                                                uint32_t length);

/**
 * @brief Get final buffer and length
 * @param writer Source writer
 * @param buffer Output buffer pointer
 * @param length Output buffer length
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_writer_get_buffer(bebop_writer_t *writer, uint8_t **buffer,
                                       size_t *length);

/** @} */

/** @defgroup utilities Utility Functions
 *  @{
 */

/**
 * @brief Parse GUID from string representation
 * @param str GUID string (with or without dashes)
 * @return Parsed GUID (zero GUID if invalid)
 */
bebop_guid_t bebop_guid_from_string(const char *str);

/**
 * @brief Format GUID as string
 * @param guid Source GUID
 * @param context Context for string allocation
 * @param out Formatted string pointer
 * @return BEBOP_OK or error code
 */
bebop_result_t bebop_guid_to_string(bebop_guid_t guid, bebop_context_t *context,
                                    char **out);

/** @} */

/** @defgroup internal Internal Arena Functions
 *  @{
 */

/**
 * @brief Allocate memory from arena (thread-safe)
 * @param arena Target arena
 * @param size Bytes to allocate
 * @return Pointer to allocated memory or NULL on failure
 */
void *bebop_arena_alloc(bebop_arena_t *arena, size_t size);

/**
 * @brief Duplicate string in arena
 * @param arena Target arena
 * @param str Source string
 * @param len String length
 * @return Null-terminated copy or NULL on failure
 */
char *bebop_arena_strdup(bebop_arena_t *arena, const char *str, size_t len);

/** @} */

/** @defgroup inline_utils Utility Functions
 *  @{
 */

/**
 * @brief Get default context options
 * @return Default configuration
 */
bebop_context_options_t bebop_context_default_options(void);

/**
 * @brief Get bytes read from reader
 * @param reader Source reader
 * @return Bytes read from start
 */
size_t bebop_reader_bytes_read(const bebop_reader_t *reader);

/**
 * @brief Get current reader position
 * @param reader Source reader
 * @return Current position pointer
 */
const uint8_t *bebop_reader_position(const bebop_reader_t *reader);

/**
 * @brief Get writer buffer length
 * @param writer Source writer
 * @return Bytes written
 */
size_t bebop_writer_length(const bebop_writer_t *writer);

/**
 * @brief Get writer remaining capacity
 * @param writer Source writer
 * @return Bytes remaining in buffer
 */
size_t bebop_writer_remaining(const bebop_writer_t *writer);

/**
 * @brief Create string view from C string
 * @param str Source C string
 * @return String view (zero length if str is NULL)
 */
bebop_string_view_t bebop_string_view_from_cstr(const char *str);

/**
 * @brief Compare string views for equality
 * @param a First string view
 * @param b Second string view
 * @return true if equal
 */
bool bebop_string_view_equal(bebop_string_view_t a, bebop_string_view_t b);

/**
 * @brief Compare GUIDs for equality
 * @param a First GUID
 * @param b Second GUID
 * @return true if equal
 */
bool bebop_guid_equal(bebop_guid_t a, bebop_guid_t b);

/**
 * @brief Allocate memory from context arena
 * @param context Source context
 * @param size Bytes to allocate
 * @return Pointer to allocated memory or NULL on failure
 */
void *bebop_context_alloc(bebop_context_t *context, size_t size);

/**
 * @brief Duplicate string in context arena
 * @param context Source context
 * @param str Source string
 * @param len String length
 * @return Null-terminated copy or NULL on failure
 */
char *bebop_context_strdup(bebop_context_t *context, const char *str,
                           size_t len);

/** @} */

/** @defgroup constants Runtime Constants
 *  @{
 */

#define BEBOP_ARENA_BLOCK_OVERHEAD \
  (sizeof(bebop_arena_block_t)) /**< Overhead per arena block */
#define BEBOP_ARENA_DEFAULT_ALIGNMENT \
  (sizeof(void *)) /**< Default memory alignment */
#define BEBOP_GUID_STRING_LENGTH \
  36 /**< GUID string length (no null terminator) */

/** Compile-time size validations */
_Static_assert(sizeof(uint8_t) == 1, "sizeof(uint8_t) should be 1");
_Static_assert(sizeof(uint16_t) == 2, "sizeof(uint16_t) should be 2");
_Static_assert(sizeof(uint32_t) == 4, "sizeof(uint32_t) should be 4");
_Static_assert(sizeof(uint64_t) == 8, "sizeof(uint64_t) should be 8");
_Static_assert(sizeof(float) == 4, "sizeof(float) should be 4");
_Static_assert(sizeof(double) == 8, "sizeof(double) should be 8");
_Static_assert(sizeof(bebop_guid_t) == 16, "sizeof(bebop_guid_t) should be 16");

/** @} */

#ifdef __cplusplus
}
#endif

#endif /* BEBOP_H */