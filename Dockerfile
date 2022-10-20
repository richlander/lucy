FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
COPY . .
RUN dotnet publish src/Lucy/Lucy.csproj -o /app

FROM mcr.microsoft.com/dotnet/runtime:7.0
COPY --from=build /app /app
WORKDIR /app
ENTRYPOINT [ "./lucy" ]
