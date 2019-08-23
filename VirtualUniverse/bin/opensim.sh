#!/bin/sh
export MONO_THREADS_PER_CPU=2000
ulimit -s unlimited
# next option may improve SGen gc (for opensim only) you may also need to increase nursery size on large regions
#export MONO_GC_PARAMS="minor=split,promotion-age=14"
mono --desktop OpenSim.exe
