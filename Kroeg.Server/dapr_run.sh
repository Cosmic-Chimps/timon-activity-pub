#dapr run --app-id timon-identity-server --app-port 5000  dotnet run

daprd --app-id "timon-activity-pub" --app-port "5010" --components-path "./components" --dapr-grpc-port "50012" --dapr-http-port "3511" "--enable-metrics=false" --placement-address "localhost:50005"
