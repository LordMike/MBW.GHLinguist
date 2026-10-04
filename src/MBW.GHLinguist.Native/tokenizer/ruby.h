/* Stands in for ruby.h when Linguist's generated tokenizer (ext/linguist/lex.linguist_yy.c) is compiled into the
 * native bridge. The scanner's FEED actions then hand each token to the classifier instead of allocating a Ruby
 * string, and the scanner itself is unchanged. */
#ifndef GHLINGUIST_TOKENIZER_RUBY_SHIM_H
#define GHLINGUIST_TOKENIZER_RUBY_SHIM_H

#include <stdint.h>

typedef uintptr_t VALUE;

#define rb_str_new ghl_tokenizer_token
#define rb_str_cat ghl_tokenizer_append

#ifdef __cplusplus
extern "C" {
#endif

VALUE ghl_tokenizer_token(const char* text, long length);
VALUE ghl_tokenizer_append(VALUE token, const char* text, long length);

#ifdef __cplusplus
}
#endif

#endif
