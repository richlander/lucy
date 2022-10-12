# Lucy -- Layer Update Check Yeoman v1.0

This tool checks to see if a container image is stale, per the image layers it relies on and should therefore be re-built.

It can work with any registry that supports [Registry API](https://docs.docker.com/registry/spec/api/). It has special support for  Docker Hub and GitHub Container Registry.

Lucy has following required syntax.

```bash
lucy targetImageAddress baseImageAddress
```

You can see this with the following examples.

```bash
$ lucy mcr.microsoft.com/dotnet/samples:aspnetapp mcr.microsoft.com/dotnet/aspnet:7.0              
fresh
$ lucy mcr.microsoft.com/dotnet/runtime:7.0 debian:bullseye-slim             
stale
$ lucy ghcr.io/richlander/dotnet-docker/aspnetapp:nightly mcr.microsoft.com/dotnet/aspnet:7.0   
fresh
```

The tool has various verbosity modes. The demonstrated verbosity is the default or `--verbosity 0`.

The following example demonstrates `--verbosity 1`.

```bash
$ lucy mcr.microsoft.com/dotnet/samples:aspnetapp mcr.microsoft.com/dotnet/aspnet:7.0 --verbosity 1
Layer Update Check Yeoman (Lucy) v1.0
Target image: mcr.microsoft.com/dotnet/samples:aspnetapp
Base image  : mcr.microsoft.com/dotnet/aspnet:7.0

Both images type: application/vnd.docker.distribution.manifest.list.v2+json
Image includes 5 manifests that will be validated.

Image state:
fresh
```

The following example demonstrates `--verbosity 2`.

```bash
$ lucy mcr.microsoft.com/dotnet/samples:aspnetapp mcr.microsoft.com/dotnet/aspnet:7.0 --verbosity 2
Layer Update Check Yeoman (Lucy) v1.0
Target image: mcr.microsoft.com/dotnet/samples:aspnetapp
Base image  : mcr.microsoft.com/dotnet/aspnet:7.0

Both images type: application/vnd.docker.distribution.manifest.list.v2+json
Image includes 5 manifests that will be validated.

Validate for: linux/amd64
Layer match: sha256:bd159e379b3b1bc0134341e4ffdeab5f966ec422ae04818bb69ecef08a823b05
Layer match: sha256:e55a07b7e890c54fa94df56be9590be6835718888c746f061dfc526ed2d529ec
Layer match: sha256:81957ac33dd22b8cc7db206b57ff1aa08540a8727d70f775509d9a18ff94f6a4
Layer match: sha256:eefb39c22ce5dbe64ad685b76b831041c95f6262ad7198f0944aebafa66175ea
Layer match: sha256:fc64b2e24534785dcaf98ff5acf03fcb1e5e261751dfbd2529a92498bc24331a

Validate for: linux/arm
Layer match: sha256:2bc64aeac2e429577e497affc8a416d2fc59158aa967f07aced793368e19455f
Layer match: sha256:9d08e29ce3eec8c7f44a3fb502900ef8c2436dcdfb450725dd7c0f3a12466930
Layer match: sha256:4d5be00e05469cd22347ba13a8842dce3d9d677ae57c3f933a6bc192c3dc6cee
Layer match: sha256:8a4730357f770d9d6431edd46c42a4bd029f957952d3c600541a203202b5fdfb
Layer match: sha256:6d8e696456b278ef2b0ae7fb5ea20ddeedd64f4935118884d84d793abb008e39

Validate for: linux/arm64
Layer match: sha256:df8e44b0463f16c791d040e02e9c3ef8ec2a84245d365f088a80a22a455c71e8
Layer match: sha256:21c9038c1a953871c0a5f731499cbf9d37d2dafe200c9dd404f1550948adcfe0
Layer match: sha256:1b471a2fe1f9ad70ffb7ef80f17c96ae8a2e7dd98e56cff1e81a754cc45fbbce
Layer match: sha256:a8fef95352377aa992b4f643b8629a421660b2189e7ba138f8272de0e3ea3a6d
Layer match: sha256:191a955a3ef70a2fccc98e41e5e36825289d32b6b1e5a6173ae1d70089791cd2

Validate for: windows:10.0.17763.3532/amd64
Layer match: sha256:5ead999142ecce15e02523c49706a633fa708374d94bb65a254e3a3c117d609b
Layer match: sha256:e17326ec6b7ff7ab34fb7d29b001527a8c7fa4ec19b01fe6c271200e688c0d07
Layer match: sha256:631a0ae392689ecc59fde470bb76f48d75bbcffb98d67198f437f3e5f4174121
Layer match: sha256:4e77dc6b3804367a8a316a32663d62088e004fb1c4ebe9d073195a3f60a1c5f3
Layer match: sha256:55ff1c4fe1d67f279dc08e16a8fbc5a723786b195fe6808881ca79407bb20ccb
Layer match: sha256:0966ea5c7a216101260c1550fd515ceeb928bb54505d964e5df6a5da346e5281
Layer match: sha256:128bee0e6c7136266de2b83359591568cb96d6e8a8ddde9d5363e70fcf306148
Layer match: sha256:00ef2c7674320260031ca90efe861454abfdc94e01104ae3f2846e87a7824769

Validate for: windows:10.0.20348.1129/amd64
Layer match: sha256:38fa349577729651ac1fc3ec785f908719a8100da5f5ba9bd3f549411061f583
Layer match: sha256:ba6a6e4eb4c99f20a2a419c2cd42a5e854ab523d89eaf0606a88f83193365d7a
Layer match: sha256:269f0b75372c644c015aecdf99408f2190103fc9cb367cc96000f851422afe5d
Layer match: sha256:c2275f439f28c1225f70a02c393547e414a097a0c4ed85e2aaf7cc1ac61f8b45
Layer match: sha256:194ddcc24e2f69642195b508a928f2f535d32183a3ccb84071dd78421b0647f1
Layer match: sha256:e99c2f84a223b79e739f5a6c182006f19ef721da331f424c19d83642d8ee4afc
Layer match: sha256:210bff8f18ee08b13debfc5a7dc03165ac4e875db16fac54b61309a14fa74379
Layer match: sha256:6ae7a7d977cc2e5664c762fe74c94ee57370dfc802ddcc73eabf64c45ddb4b32

Image state:
fresh
```

## Other features to consider

The following other features may be added.

- Call URL if image is stale (likely modeled around [`repository-dispatch`](https://github.com/orgs/community/discussions/26384)) per CLI-provided URL.
- Enable setting platform of target image (instead of assuming `linux/amd64`).
- Retry logic for registry.
- Publish as global tool.
- Publish as GitHub action.
- Move to `dotnet` org.
- 