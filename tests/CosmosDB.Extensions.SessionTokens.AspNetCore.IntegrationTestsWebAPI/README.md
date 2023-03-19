## Integration Tests Web API

This projects serves as a platform for both automated integration tests and manual end-to-end tests.

### How to Run on Kubernetes on a Desktop
The following instructions are for Windows:

#### Prerequisites
1. Create a free tier Cosmos DB account in Azure with a database named `Sandbox` and a container named `Counters`. The
   integration tests web API assumes that West US is the primary (read-write) region.
   1. In order to increase the likelihood that Cosmos DB read responses are inconsistent, enable replication to a second region.
2. Install [Docker Desktop](https://www.docker.com/products/docker-desktop).
3. Install [minikube](https://kubernetes.io/docs/tasks/tools/install-minikube/).
4. Start minikube: `minikube start`
5. Add the minikube ingress addon: `minikube addons enable ingress`

#### Building the Docker Image
1. Open a PowerShell terminal.
2. Switch to the minikube docker environment: `& minikube -p minikube docker-env --shell powershell | Invoke-Expression`
3. Publish the Docker image to minikube: `dotnet publish --os linux --arch x64 -p:PublishProfile=DefaultContainer -c Release`

#### Creating the `appconfig.secrets.json` Secret
In order to access Cosmos DB, the app server needs to know the primary read-write connection string for the account. To make
this available to the app server, we create a Kubernetes secret containing the connection string in a JSON file.

1. Create a file named `appconfig.secrets.json` in the local directory, containing:
    ```json
    {
      "CosmosDB:PrimaryConnectionString": "<your primary read-write connection string>"
    }
    ```
2. Upload the file as a Kubernetes secret called `secret-appsettings`: `kubectl create secret generic secret-appsettings --from-file=appconfig.secrets.json`

#### Starting the Web API

To start the web API with a single pod, apply the following manifest to your minikube cluster. This can easily be done using the
minikube dashboard (by running `minikube dashboard` and then clicking the `Create` button in the top right corner).

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: cosmos-sessiontokens-integrationtestswebapi
spec:
  rules:
    - host: localhost
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: cosmos-sessiontokens-integrationtestswebapi
                port:
                  number: 8080

---
apiVersion: v1
kind: Service
metadata:
  name: cosmos-sessiontokens-integrationtestswebapi
  labels:
    run: cosmos-sessiontokens-integrationtestswebapi
spec:
  type: NodePort
  ports:
    - name: http
      port: 8080
      protocol: TCP
      targetPort: http-web-svc
  selector:
    run: cosmos-sessiontokens-integrationtestswebapi

---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: cosmos-sessiontokens-integrationtestswebapi
spec:
  selector:
    matchLabels:
      run: cosmos-sessiontokens-integrationtestswebapi
  replicas: 1
  template:
    metadata:
      labels:
        run: cosmos-sessiontokens-integrationtestswebapi
    spec:
      containers:
        - name: cosmos-sessiontokens-integrationtestswebapi
          image: cosmos-sessiontokens-integrationtestswebapi:1.0.0
          imagePullPolicy: Never
          ports:
            - containerPort: 80
              name: http-web-svc
          volumeMounts:
          - name: secrets
            mountPath: /app/secrets
            readOnly: true
      volumes:
        - name: secrets
          secret:
            secretName: secret-appsettings
```

#### Accessing the Web API
In order to access the ingress controller, open a new PowerShell terminal and run `minikube tunnel`. You will now be able to access
the web API at `http://localhost/Counter`.

#### Scaling Up
The above manifest will create a deployment with a single pod. To scale up to say 4 pods, using the minikube dashboard, click
the `Workloads` tab, find the `cosmos-sessiontokens-integrationtestswebapi` deployment, click the three dots next to the
deployment name, and then click `Scale`. In the `Scale` dialog, set the `Replicas` value to 4 and click `Scale`. The
deployment scale up to 4 pods momentarily.

#### Cleaning Up
To delete everything in kubernetes, run `kubectl delete all --all` in a PowerShell terminal. (You might need to
delete the ingress controller first, using `kubectl delete ingress cosmos-sessiontokens-integrationtestswebapi`.)

To shut down minikube, run `minikube stop`.






