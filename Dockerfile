# ---------- Etapa 1: compilar y publicar ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AWSAppContainers.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

# ---------- Etapa 2: imagen final (solo runtime) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# La practica pide probar con -p 8080:80, asi que la app escucha en el 80.
ENV ASPNETCORE_HTTP_PORTS=80
# El archivo SQLite vive aqui; se monta como volumen para que sobreviva al contenedor.
ENV DB_PATH=/data/app.db
RUN mkdir -p /data
VOLUME /data
# 80 = API HTTP, 6061 = socket TCP
EXPOSE 80 6061

ENTRYPOINT ["dotnet", "AWSAppContainers.dll"]
