import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Container,
  Divider,
  ImageList,
  ImageListItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import './App.css'

const backendApiBase = (() => {
  const raw =
    import.meta.env.VITE_BACKEND_API ||
    import.meta.env.BACKEND_API ||
    'http://localhost:5050/'
  return raw.endsWith('/') ? raw.slice(0, -1) : raw
})()

const parseUrls = (data) => {
  if (Array.isArray(data)) return data
  if (Array.isArray(data?.urls)) return data.urls
  if (Array.isArray(data?.Urls)) return data.Urls
  if (Array.isArray(data?.data)) return data.data
  if (typeof data === 'string') return [data]
  return []
}

function App() {
  const [jsonInput, setJsonInput] = useState('')
  const [itemId, setItemId] = useState('')
  const [imageUrls, setImageUrls] = useState([])
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [postLoading, setPostLoading] = useState(false)
  const [getLoading, setGetLoading] = useState(false)

  const handleLoadImages = async () => {
    setError('')
    setMessage('')

    let payload
    try {
      payload = JSON.parse(jsonInput || '{}')
    } catch (err) {
      setError('Please provide valid JSON in the payload.')
      return
    }

    setPostLoading(true)
    try {
      const response = await fetch(backendApiBase, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      })

      if (!response.ok) {
        const details = await response.text()
        throw new Error(details || `Request failed (${response.status})`)
      }

      setMessage('Images sent for processing.')
    } catch (err) {
      setError(err.message || 'Failed to send images.')
    } finally {
      setPostLoading(false)
    }
  }

  const handleViewImages = async () => {
    if (!itemId.trim()) {
      setError('Enter an item id to fetch processed images.')
      return
    }

    setError('')
    setMessage('')
    setGetLoading(true)

    try {
      const response = await fetch(
        `${backendApiBase}/${encodeURIComponent(itemId.trim())}`,
      )
      if (!response.ok) {
        const details = await response.text()
        throw new Error(details || `Request failed (${response.status})`)
      }

      let result = []
      try {
        result = await response.json()
      } catch {
        result = []
      }

      const urls = parseUrls(result)
      setImageUrls(urls)
      setMessage(
        urls.length
          ? `Loaded ${urls.length} processed image${urls.length === 1 ? '' : 's'}.`
          : 'No processed images returned yet.',
      )
    } catch (err) {
      setError(err.message || 'Failed to fetch processed images.')
      setImageUrls([])
    } finally {
      setGetLoading(false)
    }
  }

  return (
    <Box className="app-shell">
      <Container maxWidth="md">
        <Paper className="panel" elevation={6}>
          <Stack spacing={3}>
            <Typography variant="h4" className="title">
              Image processor and viewer
            </Typography>
            <Typography variant="body1" color="text.secondary">
              Send raw image metadata to the API and pull back the processed image
              variants from your S3 bucket.
            </Typography>

            <Divider />

            <Stack spacing={2}>
              <Typography variant="subtitle1" className="section-heading">
                Load images payload
              </Typography>
              <TextField
                label="Image payload JSON"
                placeholder='e.g. {"ItemId":"123","S3URL":"https://...","format":"32px"}'
                multiline
                minRows={5}
                fullWidth
                value={jsonInput}
                onChange={(e) => setJsonInput(e.target.value)}
              />
              <Stack direction="row" spacing={2} justifyContent="flex-end">
                <Button
                  variant="contained"
                  size="large"
                  onClick={handleLoadImages}
                  disabled={postLoading}
                >
                  {postLoading ? 'Sending…' : 'Load Images'}
                </Button>
              </Stack>
            </Stack>

            <Divider />

            <Stack spacing={2}>
              <Typography variant="subtitle1" className="section-heading">
                View processed images
              </Typography>
              <Stack
                spacing={2}
                direction={{ xs: 'column', sm: 'row' }}
                alignItems="stretch"
              >
                <TextField
                  label="Item ID"
                  placeholder="Enter the ItemId you ingested"
                  fullWidth
                  value={itemId}
                  onChange={(e) => setItemId(e.target.value)}
                />
                <Button
                  variant="outlined"
                  color="primary"
                  size="large"
                  onClick={handleViewImages}
                  disabled={getLoading}
                >
                  {getLoading ? 'Fetching…' : 'View processed images'}
                </Button>
              </Stack>
            </Stack>

            {(message || error) && (
              <Alert
                severity={error ? 'error' : 'success'}
                sx={
                  error
                    ? undefined
                    : { backgroundColor: '#1b4332', color: '#f1f5f9' }
                }
              >
                {error || message}
              </Alert>
            )}

            {imageUrls.length > 0 && (
              <Box>
                <Typography variant="subtitle1" className="section-heading">
                  Processed image URLs
                </Typography>
                <ImageList variant="masonry" cols={3} gap={12}>
                  {imageUrls.map((url, index) => (
                    <ImageListItem key={url || index}>
                      <img src={url} alt={`Processed ${index + 1}`} loading="lazy" />
                    </ImageListItem>
                  ))}
                </ImageList>
              </Box>
            )}
          </Stack>
        </Paper>
      </Container>
    </Box>
  )
}

export default App
