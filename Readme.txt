Commands - 

1. Setup aws local profile and connect to aws environment to deploy (update profile as per local config) - 
    export AWS_PROFILE=dev_deploy && export AWS_DEFAULT_REGION=ap-southeast-2

2. Run below commands to create infrastructure (this is required because api connects to dynamodb) -
    - cd infra/terraform
        - terraform init
        - terraform plan  
        - terraform apply 
    To delete resources  
        - terraform destroy

3. Below resources are created in AWS
    api_invoke_url = "https://49ddy95rec.execute-api.us-east-1.amazonaws.com/prod"
    dynamodb_table = "Images"
    lambda_api_handler_name = "car-images-api-handler"
    lambda_queue_handler_name = "car-images-queue-handler"
    repository_bucket = "carimagesrepository1"
    sqs_queue_url = "https://sqs.us-east-1.amazonaws.com/649988449397/car-images-events"
    website_bucket = "carsimageswebsite"

4. Run lambda locally - 
    dotnet build && dotnet-lambda-test-tool-8.0
    Clean build if required -  

5. Run imageProcessor lambda - 
    dotnet build && dotnet-lambda-test-tool-8.0 --port 5051



Deploy Lambda manually
    cd backend/ImageIngestService
    dotnet restore
    dotnet publish -c Release -r linux-x64 --self-contained false -o ./bin/publish
    cd bin/publish && zip -r ../api_handler.zip . && cd ../..
    aws lambda update-function-code --function-name car-images-api-handler --zip-file fileb://bin/api_handler.zip

    cd backend/ImageProcessor
    dotnet restore
    dotnet publish -c Release -r linux-x64 --self-contained false -o ./bin/publish
    cd bin/publish && zip -r ../api_handler.zip . && cd ../..
    aws lambda update-function-code --function-name car-images-queue-handler --zip-file fileb://bin/api_handler.zip