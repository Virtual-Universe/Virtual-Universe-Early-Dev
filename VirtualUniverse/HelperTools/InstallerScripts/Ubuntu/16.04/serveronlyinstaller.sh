#!/bin/bash
# Do the distribution upgrades
sudo apt-get dist-upgrade -y
# Add package repositories for mono-project
sudo apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
echo "deb https://download.mono-project.com/repo/ubuntu stable-xenial main" | sudo tee /etc/apt/sources.list.d/mono-official-stable.list
sudo apt update
# Install Mono related packages
sudo apt-get install mono-complete -y
sudo apt-get install mono-xsp4 -y
# Install MariaDB Server
sudo apt-get install mariadb-server
# Install Git
sudo apt-get install git
# Install Virtual Universe
#cd /
#mkdir Github
#cd Github/
#git clone --recursive https://github.com/Virtual-Universe/Virtual-Universe-Early-Dev.git
#cd Virtual-Universe-Early-Dev/
#./runprebuild.sh
#msbuild