#!/bin/sh

case "$1" in

  'clean')

    mono Prebuild.exe /clean

  ;;


  'autoclean')

    echo y|mono Prebuild.exe /clean

  ;;


  *)

    mono Prebuild.exe /target nant
    mono Prebuild.exe /target vs2015

  ;;

esac