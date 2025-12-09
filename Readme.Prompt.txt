Create terraform code to 
    1. Create a C# lambda interfaced with API gateway
    2. Create a C# lambda interfaced with SQS queue
    3. Create an S3 bucket with name - carimagesrepository
    4. Create a DynamoDB table called Images with column - {ItemId, ImageId, format, url}
    5. Create a bucket for website hosting - carsimageswebsite

Write C# lambda code in ImageIngestService which has 2 endpoints - 
1. POST 
2. GET 


Create folder structure like below - 

/ImageIngestService
      ├── Functions
      │    ├── GetImageFunction.cs
      │    └── PostImageFunction.cs
      │
      ├── Services
      │    ├── SqsPublisher.cs
      │
      ├── Models
      │    ├── ImageRequest.cs
      │    ├── ImageResponse.cs
      │
      ├── Helpers
      │    ├── ResponseHelper.cs
      │
      ├── Config
      │    └── AwsSettings.cs


Update the s3 bucket 

Add functionality to PostImageFunction to handle the request.  
1. Read the post request into model created
2. Make pair of Item, URL and log it.
2.1 Before saving image in s3, create a folder in s3. folder name is ItemId
3. Copy image from Request URL and save it to S3 bucket configured in AwsSettings file.
4. Note the s3 URL
5. Create a dynamo DB entry in the Images table like below 
    Images table with column - {ItemId, ImageId, format, url}
    ImageId can be parsed from Original image URL and format is 'ORIGINAL'

After saving URLs, 
Create 4 SQS events per image URL with attribute - 
    ItemId, S3URL, format-32px
    ItemId, S3URL, format-100px
    ItemId, S3URL, format-200px
    ItemId, S3URL, format-blurred


Add functionality to GetImageFunction
1. Update Get to Get/{ItemId}
2. Generate Presigned URL for all images which are under ItemId folder
3. Return these in response


Update handler in ImageProcessor
1. Create a service folder. Create file ProcessImage. 
    Create interface ImageProcessor. Expose a method ProcessImage and derive 4 class from it - 
        ImageProcessor32Px
        ImageProcessor100Px
        ImageProcessor200Px
        ImageProcessorBlurred
    Based on the format recieved on event, create an image and save it to s3 with url -   https://carimagesrepository2.s3.amazonaws.com/12345/77ed6qf0ec26o0s98045ugsob_{format}.jpg 

Once the format is saved in DB, Save the new image created entry in dynamoDB with relevant format.


Add material ui package to website.
Add DOTENV
Add .env file with API entry 
    BACKEND_API - http://localhost:5050/
Create a component with 
    Heading - Image processor and viewer
    one Large material ui textFIeld and a material ui button (Load Images) -> the text field will accept a json and POST it to BACKEND_API

    Under the Load Images button, create another button called View processed images
    create a textbox which accepts itemid
    on Click View processed images - call Backend_API GET/{item}
    this will return s3 presigned urls.
    Create an image list to view all presigned image urls 