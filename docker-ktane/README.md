# Virtual display for KTANE in docker

## Setup

1. Drag the Linux Standalone KTANE folder into this directory and rename it to "ktane"
2. Make a "mods" directory in the new `ktane/` directory
3. Build the mod and drag the build file into `ktane/mods/`


## Build + Run

### Build:
```sh
 docker build . -t docker-ktane -f .\Dockerfile-ubuntu
```

### Run:
```sh
docker run -p 1235:1235 docker-ktane
```


## Make HTTP requests to KTANE

### Normal mod request:
```
localhost:1235/<command>?<var1>=1&<var2>=2
```

### Click on coordinates
```
localhost:1235/click?x=500&y=500
```


## Useful Commands

### Delete all containers + volumes:
```sh
docker rm -vf $(docker ps -aq)
```

### Delete all images:
```sh
docker rmi -f $(docker images -aq)
```