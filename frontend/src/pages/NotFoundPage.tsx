import { useLocation } from 'react-router'
import { StatusPage } from '../components/errors/StatusPage'

/** 404 inside the app. Status pages are always in English (project rule), whatever the interface language is. */
export function NotFoundPage() {
  const location = useLocation()
  return <StatusPage kind={404} request={`GET ${location.pathname}`} />
}
