FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
COPY . .
RUN dotnet publish src/Lucy/Lucy.csproj -o /app

FROM mcr.microsoft.com/dotnet/nightly/runtime:7.0-jammy-chiseled
COPY --from=build /app /app
WORKDIR /app
ENTRYPOINT [ "./lucy" ]