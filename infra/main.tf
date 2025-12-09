terraform {
  required_version = ">= 1.5"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    archive = {
      source  = "hashicorp/archive"
      version = "~> 2.4"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

resource "aws_s3_account_public_access_block" "this" {
  block_public_acls       = false
  block_public_policy     = false
  ignore_public_acls      = false
  restrict_public_buckets = false
}

locals {
  project_prefix      = "car-images"
  repository_bucket   = "carimagesrepository2"
  website_bucket      = "carsimageswebsite2"
  dynamodb_table_name = "Images"
}

resource "aws_s3_bucket" "repository" {
  bucket = local.repository_bucket
}

resource "aws_s3_bucket_public_access_block" "repository" {
  bucket                  = aws_s3_bucket.repository.id
  block_public_acls       = false
  block_public_policy     = false
  ignore_public_acls      = false
  restrict_public_buckets = false
}

data "aws_iam_policy_document" "repository_public_read_write" {
  statement {
    sid    = "AllowPublicReadWriteObjects"
    effect = "Allow"

    principals {
      type        = "AWS"
      identifiers = ["*"]
    }

    actions = [
      "s3:GetObject",
      "s3:PutObject",
      "s3:DeleteObject"
    ]

    resources = ["${aws_s3_bucket.repository.arn}/*"]
  }

  statement {
    sid    = "AllowPublicListBucket"
    effect = "Allow"

    principals {
      type        = "AWS"
      identifiers = ["*"]
    }

    actions   = ["s3:ListBucket"]
    resources = [aws_s3_bucket.repository.arn]
  }
}

resource "aws_s3_bucket_policy" "repository_public_read_write" {
  bucket = aws_s3_bucket.repository.id
  policy = data.aws_iam_policy_document.repository_public_read_write.json

  depends_on = [
    aws_s3_bucket_public_access_block.repository,
    aws_s3_account_public_access_block.this
  ]
}

resource "aws_s3_bucket" "website" {
  bucket = local.website_bucket

  website {
    index_document = "index.html"
    error_document = "error.html"
  }
}

resource "aws_s3_bucket_public_access_block" "website" {
  bucket                  = aws_s3_bucket.website.id
  block_public_acls       = false
  block_public_policy     = false
  ignore_public_acls      = false
  restrict_public_buckets = false
}

data "aws_iam_policy_document" "website_public_read" {
  statement {
    sid    = "AllowPublicReadForWebsite"
    effect = "Allow"

    principals {
      type        = "AWS"
      identifiers = ["*"]
    }

    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.website.arn}/*"]
  }
}

resource "aws_s3_bucket_policy" "website_public_read" {
  bucket = aws_s3_bucket.website.id
  policy = data.aws_iam_policy_document.website_public_read.json

  depends_on = [
    aws_s3_bucket_public_access_block.website,
    aws_s3_account_public_access_block.this
  ]
}

resource "aws_dynamodb_table" "images" {
  name         = local.dynamodb_table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "ImageId"

  attribute {
    name = "ImageId"
    type = "S"
  }
}

resource "aws_sqs_queue" "image_events" {
  name = "${local.project_prefix}-events"
}

data "aws_iam_policy_document" "lambda_assume_role" {
  statement {
    effect = "Allow"

    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }

    actions = ["sts:AssumeRole"]
  }
}

resource "aws_iam_role" "lambda_api_role" {
  name               = "${local.project_prefix}-api-lambda-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role" "lambda_queue_role" {
  name               = "${local.project_prefix}-queue-lambda-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume_role.json
}

resource "aws_iam_role_policy_attachment" "lambda_api_basic" {
  role       = aws_iam_role.lambda_api_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_iam_role_policy_attachment" "lambda_queue_basic" {
  role       = aws_iam_role.lambda_queue_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

data "aws_iam_policy_document" "lambda_repository_access" {
  statement {
    effect = "Allow"

    actions = [
      "s3:GetObject",
      "s3:PutObject",
      "s3:DeleteObject",
      "s3:ListBucket"
    ]

    resources = [
      aws_s3_bucket.repository.arn,
      "${aws_s3_bucket.repository.arn}/*"
    ]
  }
}

resource "aws_iam_policy" "lambda_repository_access" {
  name   = "${local.project_prefix}-repository-access"
  policy = data.aws_iam_policy_document.lambda_repository_access.json
}

resource "aws_iam_role_policy_attachment" "lambda_api_repository_access" {
  role       = aws_iam_role.lambda_api_role.name
  policy_arn = aws_iam_policy.lambda_repository_access.arn
}

resource "aws_iam_role_policy_attachment" "lambda_queue_repository_access" {
  role       = aws_iam_role.lambda_queue_role.name
  policy_arn = aws_iam_policy.lambda_repository_access.arn
}

data "aws_iam_policy_document" "lambda_queue_sqs_access" {
  statement {
    effect = "Allow"

    actions = [
      "sqs:ReceiveMessage",
      "sqs:DeleteMessage",
      "sqs:GetQueueAttributes",
      "sqs:ChangeMessageVisibility"
    ]

    resources = [aws_sqs_queue.image_events.arn]
  }
}

resource "aws_iam_policy" "lambda_queue_sqs_access" {
  name   = "${local.project_prefix}-queue-access"
  policy = data.aws_iam_policy_document.lambda_queue_sqs_access.json
}

resource "aws_iam_role_policy_attachment" "lambda_queue_sqs_access" {
  role       = aws_iam_role.lambda_queue_role.name
  policy_arn = aws_iam_policy.lambda_queue_sqs_access.arn
}

data "aws_iam_policy_document" "lambda_api_sqs_send" {
  statement {
    effect = "Allow"

    actions = [
      "sqs:SendMessage"
    ]

    resources = [aws_sqs_queue.image_events.arn]
  }
}

resource "aws_iam_policy" "lambda_api_sqs_send" {
  name   = "${local.project_prefix}-api-sqs-send"
  policy = data.aws_iam_policy_document.lambda_api_sqs_send.json
}

resource "aws_iam_role_policy_attachment" "lambda_api_sqs_send" {
  role       = aws_iam_role.lambda_api_role.name
  policy_arn = aws_iam_policy.lambda_api_sqs_send.arn
}

data "aws_iam_policy_document" "lambda_dynamodb_access" {
  statement {
    effect = "Allow"

    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:UpdateItem"
    ]

    resources = [
      aws_dynamodb_table.images.arn,
      "${aws_dynamodb_table.images.arn}/*"
    ]
  }
}

resource "aws_iam_policy" "lambda_dynamodb_access" {
  name   = "${local.project_prefix}-dynamodb-access"
  policy = data.aws_iam_policy_document.lambda_dynamodb_access.json
}

resource "aws_iam_role_policy_attachment" "lambda_api_dynamodb_access" {
  role       = aws_iam_role.lambda_api_role.name
  policy_arn = aws_iam_policy.lambda_dynamodb_access.arn
}

resource "aws_iam_role_policy_attachment" "lambda_queue_dynamodb_access" {
  role       = aws_iam_role.lambda_queue_role.name
  policy_arn = aws_iam_policy.lambda_dynamodb_access.arn
}

resource "aws_lambda_function" "api_handler" {
  function_name = "${local.project_prefix}-api-handler"
  role          = aws_iam_role.lambda_api_role.arn
  handler       = "ImageIngestService::ImageIngestLambda.Function::FunctionHandler"
  runtime       = "dotnet8"
  architectures = ["x86_64"]
  timeout       = 29

  filename         = "${path.module}/artifacts/api_handler.zip"
  source_code_hash = filebase64sha256("${path.module}/artifacts/api_handler.zip")

  environment {
    variables = {
      REPOSITORY_BUCKET = aws_s3_bucket.repository.bucket
      WEBSITE_BUCKET    = aws_s3_bucket.website.bucket
      IMAGES_TABLE      = aws_dynamodb_table.images.name
    }
  }
}

resource "aws_lambda_function" "queue_handler" {
  function_name = "${local.project_prefix}-queue-handler"
  role          = aws_iam_role.lambda_queue_role.arn
  handler       = "ImageProcessor::SqsProcessor.Function::FunctionHandler"
  runtime       = "dotnet8"
  architectures = ["x86_64"]
  timeout       = 29

  filename         = "${path.module}/artifacts/queue_handler.zip"
  source_code_hash = filebase64sha256("${path.module}/artifacts/queue_handler.zip")

  environment {
    variables = {
      REPOSITORY_BUCKET = aws_s3_bucket.repository.bucket
      WEBSITE_BUCKET    = aws_s3_bucket.website.bucket
      IMAGES_TABLE      = aws_dynamodb_table.images.name
      QUEUE_URL         = aws_sqs_queue.image_events.id
    }
  }
}

resource "aws_lambda_event_source_mapping" "queue_handler" {
  event_source_arn = aws_sqs_queue.image_events.arn
  function_name    = aws_lambda_function.queue_handler.arn
  batch_size       = 10
  enabled          = true
}

resource "aws_api_gateway_rest_api" "images" {
  name        = "${local.project_prefix}-api"
  description = "API Gateway fronting the car images Lambda"
}

resource "aws_api_gateway_resource" "images" {
  rest_api_id = aws_api_gateway_rest_api.images.id
  parent_id   = aws_api_gateway_rest_api.images.root_resource_id
  path_part   = "images"
}

resource "aws_api_gateway_method" "images_any" {
  rest_api_id   = aws_api_gateway_rest_api.images.id
  resource_id   = aws_api_gateway_resource.images.id
  http_method   = "ANY"
  authorization = "NONE"
}

resource "aws_api_gateway_integration" "images" {
  rest_api_id = aws_api_gateway_rest_api.images.id
  resource_id = aws_api_gateway_resource.images.id
  http_method = aws_api_gateway_method.images_any.http_method

  integration_http_method = "POST"
  type                    = "AWS_PROXY"
  uri                     = aws_lambda_function.api_handler.invoke_arn
  timeout_milliseconds    = 29000
}

resource "aws_api_gateway_method" "root_any" {
  rest_api_id   = aws_api_gateway_rest_api.images.id
  resource_id   = aws_api_gateway_rest_api.images.root_resource_id
  http_method   = "ANY"
  authorization = "NONE"
}

resource "aws_api_gateway_integration" "root" {
  rest_api_id = aws_api_gateway_rest_api.images.id
  resource_id = aws_api_gateway_rest_api.images.root_resource_id
  http_method = aws_api_gateway_method.root_any.http_method

  integration_http_method = "POST"
  type                    = "AWS_PROXY"
  uri                     = aws_lambda_function.api_handler.invoke_arn
  timeout_milliseconds    = 29000
}

resource "aws_api_gateway_deployment" "images" {
  rest_api_id = aws_api_gateway_rest_api.images.id

  triggers = {
    redeploy = sha1(jsonencode([
      aws_api_gateway_integration.images.uri,
      aws_api_gateway_integration.root.uri,
      aws_lambda_function.api_handler.source_code_hash
    ]))
  }

  lifecycle {
    create_before_destroy = true
  }

  depends_on = [
    aws_api_gateway_integration.images,
    aws_api_gateway_integration.root
  ]
}

resource "aws_api_gateway_stage" "prod" {
  rest_api_id   = aws_api_gateway_rest_api.images.id
  deployment_id = aws_api_gateway_deployment.images.id
  stage_name    = "prod"
}

resource "aws_lambda_permission" "api_gateway_invoke" {
  statement_id  = "AllowAPIGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.api_handler.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_api_gateway_rest_api.images.execution_arn}/*/*"
}
