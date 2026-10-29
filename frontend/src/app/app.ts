import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { StartupBar } from './shared/ui/startup-bar/startup-bar';

@Component({
  imports: [RouterOutlet, StartupBar],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {}
