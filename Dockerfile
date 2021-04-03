FROM mcr.microsoft.com/dotnet/aspnet:5.0.4 AS base
# WORKDIR /app
# COPY . .

FROM mcr.microsoft.com/dotnet/sdk:5.0.201-buster-slim-amd64 AS build
WORKDIR /build
COPY . .
RUN curl -sL https://deb.nodesource.com/setup_12.x | bash -
RUN apt install -y nodejs
RUN dotnet restore "Kroeg.Server/Kroeg.Server.csproj"

FROM build AS publish
RUN dotnet publish "Kroeg.Server/Kroeg.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS="http://*:8080"
EXPOSE 8080

RUN chmod +x run.sh

RUN addgroup --gid 998 --system appgroup \
    && adduser --uid 1004 --system appuser --ingroup appgroup

USER appuser

ENTRYPOINT ["./run.sh"]
