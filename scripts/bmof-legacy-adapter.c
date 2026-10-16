/* Scope-limited adapter for pali/bmfdec commit c7b72f6ece126a8f686d66402009a14cb032a769.
 * Compile with -I /path/to/bmfdec. Upstream code is neither copied nor modified.
 * Input: decompressed BMOF from the documented msiapcfg.dll MOFRESOURCE sample.
 * First 15 of 16 classes only; the unsupported MSI_ACPI method class is skipped.
 * Preserve full-buffer offsets so upstream qualifier flavor parsing remains valid.
 */
#define main upstream_main
#include "bmf2mof.c"
#undef main

int main(int argc, char **argv) {
    if (argc != 2) return 2;
    FILE *file = fopen(argv[1], "rb");
    if (!file) return 2;
    if (fseek(file, 0, SEEK_END) != 0) return 2;
    long size = ftell(file);
    rewind(file);
    if (size < 20 || size > 8388608) return 2;
    char *buffer = malloc(size);
    if (!buffer || fread(buffer, 1, size, file) != (size_t)size) return 2;
    fclose(file);
    uint32_t end = ((uint32_t *)buffer)[1];
    uint32_t count = ((uint32_t *)buffer)[4];
    if (((uint32_t *)buffer)[0] != 0x424D4F46 || end < 20 || end > (uint32_t)size || count != 16) return 2;
    uint32_t offset = 20;
    for (uint32_t i = 0; i < 15; ++i) {
        if (offset > end - 4) return 2;
        uint32_t length = *((uint32_t *)(buffer + offset));
        if (length < 8 || length > end - offset) return 2;
        struct mof_class definition = parse_class(buffer + offset, length, offset);
        fprintf(stdout, "// Class %u; decompressed source offset 0x%X, bytes 0x%X\n", i, offset, length);
        print_classes(stdout, &definition, 1);
        free_class(&definition);
        offset += length;
    }
    fprintf(stderr, "Skipped final MSI_ACPI class at offset 0x%X (unsupported upstream parameter parsing).\n", offset);
    free(buffer);
    return 0;
}
