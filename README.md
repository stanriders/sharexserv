[![Build status](https://ci.appveyor.com/api/projects/status/63ig8dor08q8sy2f?svg=true)](https://ci.appveyor.com/project/stanriders/sharexserv)

This server does **NOT** serve files to users, it's only purpose is to receive files.

## Config

```
listener_prefix: http://+:80/upload/
key: 'secretpassword'
path: './files/'
address: 'http://example.com/'
fail_address: 'http://example.com/failed.jpg'
store_duration: 14 # days
removal_ignore_list:
- 'index.html'
- 'style.css'
- 'failed.jpg'
only_images: true
```

---
.NET Core 2.2