#!/usr/bin/env bash

docker build . -t jjchiw/timon-activity-pub:$(git rev-parse --short HEAD)
docker push jjchiw/timon-activity-pub:$(git rev-parse --short HEAD)
git rev-parse --short HEAD
