FROM mcr.microsoft.com/dotnet/aspnet:5.0.4 AS base
WORKDIR /app
COPY . .

FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS build
WORKDIR /build
COPY . .

RUN dotnet restore "Kroeg.Server.csproj"

FROM build AS publish
RUN dotnet publish "Kroeg.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN cp .MyCertificate.crt /usr/local/share/ca-certificates
RUN update-ca-certificates

RUN chmod 777 .MyCertificate.pfx

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS="http://*:8080"
EXPOSE 8443

RUN addgroup --gid 998 --system appgroup \
    && adduser --uid 1004 --system appuser --ingroup appgroup

USER appuser

ENTRYPOINT ["dotnet", "Kroeg.Server.dll"]
