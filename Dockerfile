FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
COPY . .
RUN dotnet publish src/Lucy/Lucy.csproj -o /app

FROM mcr.microsoft.com/dotnet/runtime:7.0
RUN apt update && apt install -y jq
COPY --from=build /app /lucy
WORKDIR /lucy
ENTRYPOINT [ "./lucy" ]
