output "api_invoke_url" {
  description = "Invoke URL for the API Gateway stage."
  value       = aws_api_gateway_stage.prod.invoke_url
}

output "sqs_queue_url" {
  description = "URL for the image events SQS queue."
  value       = aws_sqs_queue.image_events.id
}

output "repository_bucket" {
  description = "S3 bucket used to store car images."
  value       = aws_s3_bucket.repository.bucket
}

output "website_bucket" {
  description = "S3 bucket configured for static website hosting."
  value       = aws_s3_bucket.website.bucket
}

output "dynamodb_table" {
  description = "DynamoDB table for image metadata."
  value       = aws_dynamodb_table.images.name
}

output "lambda_api_handler_name" {
  description = "Lambda function handling API Gateway requests."
  value       = aws_lambda_function.api_handler.function_name
}

output "lambda_queue_handler_name" {
  description = "Lambda function processing SQS messages."
  value       = aws_lambda_function.queue_handler.function_name
}
