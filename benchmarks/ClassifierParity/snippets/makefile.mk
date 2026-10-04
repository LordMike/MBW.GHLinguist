CC ?= cc
all: demo
demo: demo.o
	$(CC) -o $@ $^
.PHONY: all
