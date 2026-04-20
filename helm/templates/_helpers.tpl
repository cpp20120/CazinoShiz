{{- define "casinoshiz.fullname" -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- end }}

{{- define "casinoshiz.labels" -}}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
{{ include "casinoshiz.selectorLabels" . }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{- define "casinoshiz.selectorLabels" -}}
app.kubernetes.io/name: casinoshiz
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}
