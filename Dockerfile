FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ServiceDesk.slnx ./
COPY src/ServiceDesk.Core/ServiceDesk.Core.csproj src/ServiceDesk.Core/
COPY src/ServiceDesk.Infrastructure/ServiceDesk.Infrastructure.csproj src/ServiceDesk.Infrastructure/
COPY src/ServiceDesk.Api/ServiceDesk.Api.csproj src/ServiceDesk.Api/
COPY tests/ServiceDesk.Core.Tests/ServiceDesk.Core.Tests.csproj tests/ServiceDesk.Core.Tests/
COPY tests/ServiceDesk.Api.Tests/ServiceDesk.Api.Tests.csproj tests/ServiceDesk.Api.Tests/
RUN dotnet restore ServiceDesk.slnx

COPY . .
RUN dotnet publish src/ServiceDesk.Api/ServiceDesk.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ServiceDesk.Api.dll"]
