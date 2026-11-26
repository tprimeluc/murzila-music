FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Сервер включает файлы из соседнего MurzilaMusic.Shared — копируем весь src.
COPY src/ ./
RUN dotnet restore MurzilaMusic.Server/MurzilaMusic.Server.csproj
RUN dotnet publish MurzilaMusic.Server/MurzilaMusic.Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV PORT=10000
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV MURZILA_CLOUD_DIR=/app/data/cloud
ENV MURZILA_DB=/app/data/server.db
# Бесплатное распознавание через AcoustID (без карты): fpcalc + ffmpeg
RUN apt-get update && apt-get install -y --no-install-recommends \
    libchromaprint-tools ffmpeg && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /app/data/cloud
EXPOSE 10000
ENTRYPOINT ["dotnet", "MurzilaMusic.Server.dll"]
