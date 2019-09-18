# hey, emacs! this is a -*- makefile -*-
#
# Universe makefile
#

RUBY    = $(strip $(shell which ruby 2>/dev/null))
ifeq ($(RUBY),)
NANT    = nant
else
NANT	= $(shell if test "$$EMACS" = "t" ; then echo "nant"; else echo "./nant-color"; fi)
endif

all: prebuild
	# @export PATH=/usr/local/bin:$(PATH)
	${NANT}
	find Universe -name \*.mdb -exec cp {} bin \; 

release: prebuild
	${NANT} -D:project.config=Release
	find Universe -name \*.mdb -exec cp {} bin \;

prebuild:
	./runprebuild.sh

clean:
	# @export PATH=/usr/local/bin:$(PATH)
	-${NANT} clean

test: prebuild
	${NANT} test

test-xml: prebuild
	${NANT} test-xml

tags:
	find Universe -name \*\.cs | xargs etags 

cscope-tags:
	find Universe -name \*\.cs -fprint cscope.files
	cscope -b

include $(wildcard Makefile.local)

